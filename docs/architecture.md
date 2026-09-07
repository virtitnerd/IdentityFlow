# Architecture

## Why this shape

Entra ID does not run a SCIM *server* that a third party HR system can push
into directly. What it does have, purpose-built for exactly this scenario,
is **[API-driven inbound provisioning](https://learn.microsoft.com/entra/identity/app-provisioning/inbound-provisioning-api-concepts)**:
an Enterprise Application (installed from the Entra gallery) that exposes a
`bulkUpload` Graph endpoint accepting SCIM-shaped records. You configure
attribute mappings and matching rules once in the Entra admin center, and
the *provisioning service itself* decides whether an incoming record is a
create, update, enable, or disable - the caller just pushes current-state
records. That's the backbone of this solution instead of a hand-rolled SCIM
server or a from-scratch Graph user-CRUD pipeline.

Paycom does not publish a public API - access, base URL, and the exact
report/field layout are negotiated per customer with a Paycom
representative. So the Paycom side is built as a thin, configurable
adapter (`PaycomEntraProvisioner.Paycom`) that normalizes whatever comes
back into a canonical `EmployeeRecord`, rather than hard-coding a specific
contract.

Assigned (non-dynamic) security groups sit outside what the provisioning
job manages, so a second, direct Microsoft Graph client
(`PaycomEntraProvisioner.Graph`) reconciles those based on configurable
rules. Wherever possible, prefer an Entra **dynamic-membership group**
instead - once an attribute (including an extension attribute) is synced,
Entra evaluates dynamic membership automatically and this solution doesn't
need to touch it at all.

## Components

```
                          ┌─────────────────────────┐
                          │   Paycom HR / Payroll    │
                          │  (Employee Master report)│
                          └────────────┬─────────────┘
                                       │ REST (SID/token or OAuth2)
                                       ▼
┌───────────────────────────────────────────────────────────────────────┐
│                      PaycomEntraProvisioner.Core                       │
│  EmployeeRecord · FieldMapping/GroupAssignmentRule · MappingEngine ·   │
│  GroupRuleEvaluator · SyncOrchestrator (the pipeline both hosts call)  │
└───────┬───────────────────────────┬──────────────────────┬───────────┘
        │                           │                      │
        ▼                           ▼                      ▼
 Paycom client              Entra provisioning       Entra directory
 (Paycom project)           client (bulkUpload)       client (Graph SDK)
        │                           │                      │
        │                           ▼                      ▼
        │                 Entra ID API-driven      Users / Groups (assigned
        │                 inbound provisioning       group reconciliation)
        │                 job → creates/updates/
        │                 disables users, writes
        │                 extension attributes
        ▼
   (data flows in;
   nothing is written
   back to Paycom)

        Azure Function (Timer + HTTP trigger)   Razor Pages admin UI
        runs SyncOrchestrator on a schedule      runs the same
        and via a secured HTTP endpoint          SyncOrchestrator in-process
                    │                                     │
                    └───────────────┬─────────────────────┘
                                    ▼
                     Azure SQL: field mappings, group rules,
                     sync run history, employee snapshots
```

- **PaycomEntraProvisioner.Core** - domain models, the mapping engine that
  turns a Paycom record + configured `FieldMapping`s into a SCIM resource,
  the group rule evaluator (`System.Linq.Dynamic.Core` expressions), and
  `SyncOrchestrator`, which is the single pipeline both hosts call so
  scheduled and manual runs never diverge.
- **PaycomEntraProvisioner.Paycom** - `IPaycomClient` implementation.
  Configurable field aliasing (`PaycomClientOptions.CoreFieldAliases`) so
  it can adapt to whatever report layout your Paycom rep provisions,
  without code changes.
- **PaycomEntraProvisioner.Graph** - `IEntraProvisioningClient` (bulkUpload,
  chunked to the documented 50-ops/call, throttled to 40 calls/5s) and
  `IEntraDirectoryClient` (user lookup + assigned-group reconciliation via
  the Microsoft Graph SDK).
- **PaycomEntraProvisioner.Data** - EF Core (Azure SQL) persistence for
  field mappings, group rules, sync run history, employee snapshots
  (used to detect a worker vanishing from the Paycom feed entirely), and a
  catalog of Paycom field names actually observed across runs - the admin
  UI uses it (plus a static list of standard Entra attributes and a live
  Graph lookup of registered custom directory extensions) to offer
  autocomplete suggestions on the Field Mappings page rather than requiring
  the source/target field names to be typed from memory.
- **PaycomEntraProvisioner.Functions** - Azure Functions isolated worker.
  `TimerSync` runs the pipeline on a configurable NCRONTAB schedule;
  `ManualSync` is a function-key-secured HTTP entry point for automation.
- **PaycomEntraProvisioner.Web** - Razor Pages admin/monitoring UI, secured
  with Entra ID sign-in (a separate, minimally-privileged app registration
  from the one used for Graph calls). Dashboard, sync history with
  per-employee drill-down, and CRUD for field mappings and group rules. Its
  "Run Now" button calls `SyncOrchestrator` in-process (same DI graph, same
  code path as the Function) rather than making a network call.

## Data flow per run

1. Before anything else, `SyncOrchestrator.RunAsync` tries to confirm any
   records a *previous* run left in the `Submitted` (pending) state,
   against the Entra provisioning audit log - see **Provisioning
   confirmation reconciliation** below. This step is skipped entirely
   (no Graph call at all) once nothing is pending, and never blocks the
   rest of the run if it fails.
2. `SyncOrchestrator.RunAsync` pulls every worker from Paycom.
3. Each provisionable worker (has a work email - status-independent, since
   a Terminated worker still needs to flow through to get disabled) is
   mapped to a SCIM `Operations` entry via configured `FieldMapping`s,
   including extension attributes.
4. Anyone present in the *previous* run's snapshot but absent from this
   pull - and not already flagged Terminated - gets a defensive `active:
   false` operation queued and a prominent `VanishedFromFeed-AutoDisabled`
   result, since Paycom omitting a worker entirely (rather than sending
   Terminated) can't be distinguished from a feed problem without a human
   look.
5. All operations are submitted to the Entra provisioning job's
   `bulkUpload` endpoint in batches, respecting documented rate limits.
   Anything the synchronous response doesn't explicitly reject is recorded
   as `Submitted` (pending), not assumed successful outright - see below.
6. Group assignment rules are evaluated for every provisionable worker;
   desired membership per assigned group is diffed against current Graph
   membership and reconciled.
7. Outcomes (per employee and per group) and run-level counters are
   recorded to `SyncRun` / `SyncRunEmployeeResult` for the admin UI.
8. The current employee set is snapshotted (full replace) for the next
   run's vanished-worker check.

A `dryRun: true` run does everything above except call Graph: it reports
what *would* be submitted and which groups *would* change, for safe
first-time validation of new field mappings or group rules.

## Security model

- Two separate Entra app registrations, least privilege on each:
  1. **Admin UI sign-in** app - delegated `User.Read` only, used solely to
     authenticate admins into the Razor Pages site.
  2. **Sync service** app - application (client-credentials) permissions:
     the provisioning job's `SynchronizationData-User.Upload` app role,
     plus `User.Read.All` and `Group.ReadWrite.All` for directory lookups
     and assigned-group reconciliation. Used server-side only (by the
     Function and by the Web app's in-process orchestrator call) - never
     exposed to a browser.
- All secrets (Paycom credentials, the sync app's client secret, the SQL
  connection string) live in Key Vault; app settings reference them via
  `@Microsoft.KeyVault(...)`, never inline.
- Both compute hosts use system-assigned managed identities: for Key Vault
  access (RBAC, `Key Vault Secrets User`) and for Azure SQL (Entra-only
  auth, no SQL logins at all).
- The Razor Pages UI requires authentication plus a specific Entra app
  role (`ProvisionerAdmin` by default) on every page via a fallback
  authorization policy - there is no anonymous or read-only surface.

## Provisioning confirmation reconciliation

Microsoft's own official sample
([AzureAD/entra-id-inbound-provisioning](https://github.com/AzureAD/entra-id-inbound-provisioning),
`PowerShell/CSV2SCIM/src/CSV2SCIM.ps1`) does **not treat the bulkUpload
POST's synchronous response as the source of truth for per-record
success/failure**. It submits records essentially fire-and-forget, then
separately queries the **asynchronous provisioning audit log**
(`Get-MgAuditLogProvisioning` / `GET /auditLogs/provisioning`) afterward
and tabulates outcomes from that. This solution follows the same pattern:

1. `SubmitOperationsAsync` only trusts the synchronous bulkUpload response
   when Entra explicitly returns a per-operation error status for a
   specific record - a real, immediate signal worth acting on. Anything
   else (a success code, *or no per-operation result at all*, which per
   Microsoft's own sample is the common case) is recorded as
   `SyncOutcomes.Submitted` - accepted, outcome not yet confirmed - rather
   than optimistically "succeeded" or pessimistically "failed."
2. At the *start* of the next run (any trigger - timer, manual, or API),
   `SyncOrchestrator.ReconcilePendingProvisioningConfirmationsAsync` looks
   for any `Submitted` results left over from previous runs. If there are
   none, it returns immediately without calling Graph at all - the steady-
   state cost of this mechanism is zero once confirmations catch up.
3. If there are pending results, it reads
   `EntraProvisioningClient.GetRecentProvisioningLogAsync` over a 72-hour
   lookback window (generous on purpose - covers a missed run or a slow
   provisioning cycle over a weekend, and costs nothing extra since the
   call only happens when needed), matches log entries back to pending
   results by employee code (the SCIM record's `externalId`), and updates
   each to `Provisioned` (confirmed success), `ProvisioningSkipped`
   (Entra decided no change was needed - not a failure), or
   `SubmissionError` (confirmed failure, with the log's real error
   message) via one batched `ISyncRunStore.ApplyProvisioningConfirmationsAsync`
   call. A result with no matching log entry yet is left `Submitted` and
   retried on a later run.

This means `RecordsSubmitted` on a given run is provisional, not final -
some of what a run counts as submitted may later resolve to
`SubmissionError` once the audit log catches up, and the reverse (a
missing per-op result that turns out fine) no longer gets miscounted as a
failure in the first place. Drill into a specific `SyncRun`'s employee
results in the admin UI to see current, possibly-still-settling outcomes
for that run; a `Submitted` result appearing on more than one or two
consecutive future runs' reconciliation passes without resolving is worth
investigating directly (a wrong matching attribute, a provisioning job
that's paused, etc.).

## Other design decisions the same research validated or challenged

- **Expression caching**: [microsoft/RulesEngine](https://github.com/microsoft/RulesEngine)
  (a comparable admin-rule-evaluation library) hit and fixed the exact
  "don't recompile a Dynamic LINQ expression on every evaluation" problem
  `DynamicExpressionEvaluator` now addresses here, via the same
  compiled-delegate-cache approach - independent validation that this was
  worth doing, not premature optimization.
- **Free-text expressions vs. a structured rule builder**: `FieldMapping.TransformExpression`
  and `GroupAssignmentRule.Condition` are free-text `System.Linq.Dynamic.Core`
  expressions, which carries the CVE-2023-32571-class of risk as an
  ongoing "stay current on this dependency" obligation rather than a
  one-time fix (see [runxc1/MicroRuleEngine](https://github.com/runxc1/MicroRuleEngine)
  for a structurally different design - rules as a `{Field, Operator,
  Value}` POCO tree compiled straight to an `Expression`, with no
  string-parsing surface at all, immune to that whole vulnerability class
  by construction). Kept the free-text design deliberately for admin
  expressiveness, since the expression fields are only reachable by an
  already-authenticated `ProvisionerAdmin`, not untrusted input - but it's
  a real trade-off, not the only reasonable choice, and worth revisiting
  if the admin population ever widens.
- **Separate Function App vs. one combined ASP.NET Core app**: no prior art
  was found for this exact split (Function for the scheduled worker,
  separate Razor Pages app for admin UI, shared Core/EF Core). Background-
  jobs literature (Azure Architecture Center, and independent 2026
  comparisons) consistently frames a single ASP.NET Core app - Razor Pages
  plus an `IHostedService`/Hangfire/Quartz.NET-scheduled job, one
  `DbContext`, one deployment - as the more commonly recommended shape
  specifically for "a scheduled job with an admin UI over the same data,"
  since it avoids running two deployables against one EF Core model and
  lets the admin UI show live run status without polling a separate app's
  logs. This solution keeps the Function split per the original
  requirement (an Azure Function was an explicit ask), but it's worth
  knowing that's a deliberate choice against the more commonly recommended
  default for this exact problem shape, not the only reasonable one.
