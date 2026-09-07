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

1. `SyncOrchestrator.RunAsync` pulls every worker from Paycom.
2. Each provisionable worker (has a work email - status-independent, since
   a Terminated worker still needs to flow through to get disabled) is
   mapped to a SCIM `Operations` entry via configured `FieldMapping`s,
   including extension attributes.
3. Anyone present in the *previous* run's snapshot but absent from this
   pull - and not already flagged Terminated - gets a defensive `active:
   false` operation queued and a prominent `VanishedFromFeed-AutoDisabled`
   result, since Paycom omitting a worker entirely (rather than sending
   Terminated) can't be distinguished from a feed problem without a human
   look.
4. All operations are submitted to the Entra provisioning job's
   `bulkUpload` endpoint in batches, respecting documented rate limits.
5. Group assignment rules are evaluated for every provisionable worker;
   desired membership per assigned group is diffed against current Graph
   membership and reconciled.
6. Outcomes (per employee and per group) and run-level counters are
   recorded to `SyncRun` / `SyncRunEmployeeResult` for the admin UI.
7. The current employee set is snapshotted (full replace) for the next
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

## Known gap: bulkUpload's synchronous response may not be a reliable per-record result

A research pass through Microsoft's own official sample
([AzureAD/entra-id-inbound-provisioning](https://github.com/AzureAD/entra-id-inbound-provisioning),
`PowerShell/CSV2SCIM/src/CSV2SCIM.ps1`) found that Microsoft's own reference
implementation **does not treat the bulkUpload POST's synchronous response
as the source of truth for per-record success/failure**. Instead, it
submits records essentially fire-and-forget, then separately queries the
**asynchronous provisioning audit log**
(`Get-MgAuditLogProvisioning` / `GET /auditLogs/provisioning`, filtered by
cycle ID) afterward and tabulates outcomes from that.

This solution currently does the opposite: `SyncOrchestrator.SubmitOperationsAsync`
treats `ScimBulkOperationResult.Status.Code` from the synchronous response as
the real per-employee outcome, marking anything that doesn't come back
"200"/"201"/"202"/"204" as `SubmissionError`. If Entra's synchronous
response is as unreliable for per-record status as Microsoft's own sample
implies, some fraction of `SubmissionError` outcomes in the run history
could be false negatives - records that actually succeeded once the
provisioning cycle finished, just not reflected in the immediate response.

`EntraProvisioningClient.GetRecentProvisioningLogAsync` already exists for
exactly this purpose (reading `/auditLogs/provisioning`) but isn't wired
into `SyncOrchestrator` or the admin UI yet - it's unused dead code today.
Closing this gap means adding a follow-up reconciliation step (immediately,
or on the *next* run) that fetches the provisioning log for the prior
cycle and corrects any `SubmissionError` outcomes the log shows actually
succeeded. Not yet implemented - flagging here since it's a real,
sourced finding rather than a guess, and worth prioritizing before
leaning on `SubmissionError` counts for anything operationally important.

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
