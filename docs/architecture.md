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
adapter (`IdentityFlow.Clients.Paycom`) behind the HR-system-agnostic
`IHrClient` interface, normalizing whatever comes back into a canonical
`EmployeeRecord` rather than hard-coding a specific contract. Paycom is the
only configured `IHrClient` implementation today - `IdentityFlow.Clients`
is where a second HR system's client would live if one is ever needed,
without Core or anything downstream of `IHrClient` changing at all.

Assigned (non-dynamic) security groups sit outside what the provisioning
job manages, so a second, direct Microsoft Graph client
(`IdentityFlow.Graph`) reconciles those based on configurable
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
│                           IdentityFlow.Core                           │
│  EmployeeRecord · FieldMapping/GroupAssignmentRule · MappingEngine ·   │
│  GroupRuleEvaluator · SyncOrchestrator (the pipeline both hosts call)  │
└───────┬───────────────────────────┬──────────────────────┬───────────┘
        │                           │                      │
        ▼                           ▼                      ▼
 Paycom client              Entra provisioning       Entra directory
 (IdentityFlow.Clients)     client (bulkUpload)       client (Graph SDK)
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

- **IdentityFlow.Core** - domain models, the mapping engine that
  turns a Paycom record + configured `FieldMapping`s into a SCIM resource,
  the group rule evaluator (`System.Linq.Dynamic.Core` expressions), and
  `SyncOrchestrator`, which is the single pipeline both hosts call so
  scheduled and manual runs never diverge.
- **IdentityFlow.Clients** - HR system clients behind Core's `IHrClient`
  interface. `IdentityFlow.Clients.Paycom` is the only implementation
  today - configurable field aliasing (`PaycomClientOptions.CoreFieldAliases`)
  lets it adapt to whatever report layout your Paycom rep provisions
  without code changes. A second HR system later means a sibling
  `IdentityFlow.Clients.<X>` implementation, registered in place of (or
  alongside) Paycom's from `Program.cs` - nothing above `IHrClient` needs
  to know or care which HR system it's actually talking to.
- **IdentityFlow.Graph** - `IEntraProvisioningClient` (bulkUpload,
  chunked to the documented 50-ops/call, throttled to 40 calls/5s) and
  `IEntraDirectoryClient` (user lookup + assigned-group reconciliation via
  the Microsoft Graph SDK).
- **IdentityFlow.Data** - EF Core (Azure SQL) persistence for
  field mappings, group rules, sync run history, employee snapshots
  (used to detect a worker vanishing from the Paycom feed entirely), a
  catalog of Paycom field names actually observed across runs - the admin
  UI uses it (plus a static list of standard Entra attributes and a live
  Graph lookup of registered custom directory extensions) to offer
  autocomplete suggestions on the Field Mappings page rather than requiring
  the source/target field names to be typed from memory - and the
  configurable Lifecycle Policy tasks plus their per-employee execution
  history (see **Lifecycle Policy Engine** below).
- **IdentityFlow.Functions** - Azure Functions isolated worker.
  `TimerSync` runs the pipeline on a configurable NCRONTAB schedule;
  `ManualSync` is a function-key-secured HTTP entry point for automation.
- **IdentityFlow.Web** - Razor Pages admin/monitoring UI, secured
  with Entra ID sign-in (a separate, minimally-privileged app registration
  from the one used for Graph calls). Dashboard, sync history with
  per-employee drill-down, and CRUD for field mappings, group rules, and
  Lifecycle Policy tasks (plus their execution history). Its "Run Now"
  button calls `SyncOrchestrator` in-process (same DI graph, same code
  path as the Function) rather than making a network call.

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
     plus `Group.ReadWrite.All` for directory lookups and assigned-group
     reconciliation, and `User.ReadWrite.All` (only strictly needed for the
     Lifecycle Policy Engine's `RevokeSignInSessions`/`DeleteAccount`
     leaver tasks - `User.Read.All` is enough if those two task types stay
     disabled). Used server-side only (by the Function and by the Web
     app's in-process orchestrator call) - never exposed to a browser.
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

## Lifecycle Policy Engine

Different organizations need materially different Joiner/Leaver behavior
for the same underlying event - a HIPAA-covered org might require an
account to survive 90 days past termination before deletion (for records-
retention reasons), while another wants sessions revoked and group
memberships stripped the same day. Rather than hard-coding one policy,
`LifecycleTask` (Admin Portal: **Lifecycle Policy**) lets an admin configure,
per `LifecycleTrigger` (`Joiner`/`Leaver`) and `LifecycleTaskType`, whether a
task is enabled and how many days to offset it from the triggering event
date - the same "days from event" trigger model
[Entra ID Governance Lifecycle Workflows](https://learn.microsoft.com/entra/id-governance/what-are-lifecycle-workflows)
uses natively, scoped down to what this pipeline can actually execute
against Paycom-sourced data rather than the full Lifecycle Workflows
template/task catalog. This is a configurable *mechanism*, not a compliance
certification - nothing in this solution asserts that a given configuration
satisfies NIST, a specific STIG, or HIPAA; that determination belongs to
whoever owns the org's compliance program.

Two task types are **continuous state**, not one-time actions, and are
evaluated fresh on every run rather than tracked as "done":

- `EnableAccount` (Joiner) - `MappingEngine.ComputeActive` holds a
  `PreHire` worker's SCIM `active` flag `false` until
  `HireDate + DayOffset` has been reached, so a new hire entered into
  Paycom ahead of their start date doesn't get a live, sign-in-capable
  account before day one.
- `DisableAccount` (Leaver) - symmetrically, a `Terminated` worker's
  `active` flag stays `true` until `TerminationDate + DayOffset`, covering
  an org's notice-period policy (e.g. access continues through a 2-week
  notice period) instead of disabling the instant the status flips.

The remaining `Leaver`-only task types are **one-time actions**, executed
by `SyncOrchestrator.RunLeaverTasksAsync` and tracked per (task, employee)
in `LifecycleTaskExecution` so each runs *at most once, ever* per employee -
idempotent even if a task's due date is reached across several consecutive
runs before it's picked up:

- `RevokeSignInSessions` - `POST /users/{id}/revokeSignInSessions`. Disabling
  an account (`accountEnabled: false`) does **not** invalidate already-issued
  refresh tokens or active sessions on its own; this is the explicit action
  that does.
- `RemoveFromAllAssignedGroups` - walks the user's `memberOf`, skips any
  group with a `membershipRule` (dynamic groups self-correct once the
  disabling sync runs - removing a member from one directly would just be
  overwritten), and removes assigned-group memberships directly.
- `DeleteAccount` - `DELETE /users/{id}`. Off by default and expected to
  stay off unless an org's retention policy calls for actual deletion after
  a defined number of days (Entra soft-deletes for a recoverable 30-day
  window regardless). Kept as a distinct, separately-enableable task from
  disablement specifically so "disable now, delete later" retention windows
  are expressible without extra code.

The trigger date for a one-time task is the employee's `TerminationDate`
when Paycom explicitly reports them `Terminated`, or - for a worker who
simply vanishes from the feed without ever being marked `Terminated` - the
same last-seen-date fallback the vanished-employee auto-disable safety net
already uses. A `dryRun` run previews which (task, employee) pairs are due
without executing or recording anything, mirroring how dry runs behave
elsewhere in this pipeline.

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
