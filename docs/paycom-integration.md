# Paycom integration notes

Paycom does not publish a public API or a single documented contract.
Access - the base URL, the report/endpoint that returns employee data, the
exact field names, and whether you get SID+API-token or OAuth2 credentials -
is negotiated per customer with your Paycom representative. Ask them
specifically for:

- **API access to an employee master / demographic report** covering the
  fields you intend to sync (name, work email, department, job title,
  employee status, hire/termination dates, manager, cost center, work
  location, and any custom fields you want to land in Entra extension
  attributes).
- Whether access is via **SID + API token** (sent as the `APISID` /
  `APIToken` headers) or **OAuth2 client credentials** - both are supported
  by `PaycomHttpClient`, toggled with `Paycom:AuthMode`.
- The **exact field names** the report returns, and whether the response is
  a bare JSON array or wrapped (e.g. `{ "data": [...] }`).

## Wiring up your tenant's actual field names

Nothing about your Paycom report layout is hard-coded. Two things adapt it:

1. **`PaycomClientOptions.CoreFieldAliases`** (in
   `PaycomEntraProvisioner.Paycom/PaycomClientOptions.cs`, overridable via
   configuration/Key Vault) maps each canonical `EmployeeRecord` property
   (`WorkEmail`, `Department`, `Status`, ...) to whatever field name Paycom
   actually returns for your report. The defaults are a best guess at a
   typical Paycom "Employee Master File" layout - replace them with your
   tenant's real field names once you have API access.
2. **`PaycomClientOptions.StatusValueMap`** maps Paycom's raw status
   code/text to this solution's normalized `EmploymentStatus` enum
   (`Active`, `OnLeave`, `Terminated`, `PreHire`).

Every field Paycom returns - not just the ones in `CoreFieldAliases` - lands
in `EmployeeRecord.RawFields` verbatim, so a `FieldMapping` in the admin UI
can reference any Paycom field directly by its native name, including ones
this client doesn't know about ahead of time.

## Validating against the real API

Before pointing this at production:

1. Get a sample response from your Paycom rep or a test call, and confirm
   `PaycomClientOptions.ResponseArrayProperty` matches how the array is
   wrapped (or leave it null if the response is a bare array - the client
   also falls back to the first array-valued property it finds).
2. Update `CoreFieldAliases` and `StatusValueMap` to match the real field
   names/values.
3. Run a **dry run** sync from the admin dashboard and review the
   per-employee preview before enabling the scheduled Function trigger.

## Webhooks

Some Paycom integration tiers expose webhooks for near-real-time change
notification. This solution currently polls on a timer (default hourly,
`SyncSchedule` app setting) rather than consuming webhooks, since webhook
payload shape isn't part of Paycom's public documentation and varies by
integration agreement. If your tenant has webhook access, the cleanest
extension point is an additional HTTP-triggered Azure Function that
validates the webhook payload and calls `SyncOrchestrator.RunAsync` the
same way `ManualSyncFunction` does, rather than polling more frequently.
