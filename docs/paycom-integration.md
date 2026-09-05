# Paycom integration notes

Paycom does not publish a public developer portal - access is provisioned
per customer by a Paycom representative. The facts below are confirmed from
Paycom's own **"API Companion Guide" (2026)** provided by a Paycom
customer, not guessed from third-party aggregator sites. Where something
is still unconfirmed for your specific tenant, it's called out explicitly.

## Confirmed facts

- **Protocol**: REST, JSON.
- **Auth**: HTTP **Basic Authentication** - SID as username, API token as
  password, standard `Authorization: Basic base64(SID:Token)` header.
  (Not custom `APISID`/`APIToken` headers - an earlier version of this doc
  had that wrong based on secondary sources; `PaycomHttpClient` now sends
  real Basic Auth.) OAuth2 client credentials is also supported for some
  agreement types (`Paycom:AuthMode = OAuth2`), but Basic Auth is the
  default and the one Paycom's own guide walks through.
- **Base URL** - one of three regional endpoints, whichever hosts your
  organization's data (confirm with your rep):
  - OKC: `https://api.paycomonline.net/v4/rest/index.php/`
  - PHX: `https://api.phx.us-west.paycomonline.net/v4/rest/index.php/`
  - DFW: `https://api.paycomdfw.net/v4/rest/index.php/`
- **Employee data endpoint**: `GET api/v1/employeedirectory` returns "a
  list of all employees and their basic demographics" - this is
  `PaycomClientOptions.EmployeeReportPath`'s default now. Supports query
  filters, e.g. `?eestatus=A&deptcode=SALES`.
- **Response envelope**: every sample in Paycom's guide wraps its payload
  as `{ "result": true, "data": [...], "errors": [], "errorCount": 0 }` -
  `ResponseArrayProperty` now defaults to `"data"` accordingly.
- **Pagination**: `pagesize` (max 500) + `page` query params. A response
  with more records than fit comes back as **HTTP 206 Partial Content**
  rather than a distinct "hasMore" flag; also watch for the `X-Total-Count`
  / `X-Max-Page-Size` response headers and a `Link` header with
  `rel="next"/"prev"/"first"/"last"`. `PaycomHttpClient` now pages
  automatically (keeps requesting until a page comes back smaller than
  `PageSize`).
- **Employee identifier**: the field is called **`eecode`** in every
  sample response, including the audit/change log - this is now the
  confirmed default for `CoreFieldAliases["EmployeeCode"]`.
- **Employee name**: the one sample that includes a name
  (`"eename": "BLACK, JACK A"`) shows it as a single combined
  "Last, First MI" string, not separate first/last fields. **Confirm
  whether `employeedirectory` splits the name or not** before assuming
  `FirstName`/`LastName` map to distinct source fields - if it's combined,
  alias both to the same source field and use a `FieldMapping.TransformExpression`
  to split it, e.g. `value.Split(", ")[1].Split(" ")[0]` for a given name.
- **IP allow-listing is required**. Paycom only accepts connections from
  IPs you've pre-registered - "Paycom can allow-list a large range to
  accommodate dynamic IPs" but you still have to provide the range(s)
  before you get your SID/token. This matters for the Azure Function: a
  Consumption-plan Function App's outbound IPs aren't a single stable
  address by default. Either register all of the Function App's
  `possibleOutboundIpAddresses` (`az functionapp show -g <rg> -n <name>
  --query possibleOutboundIpAddresses`) with Paycom, or put the Function on
  a VNet with a NAT Gateway for one static egress IP - the latter is worth
  it if Paycom's allow-list process is a hassle to update.
- **Rate limits**: endpoint-specific but "generally very high - 30,000+
  calls per day for most endpoints" per Paycom's guide. Not a practical
  constraint for an hourly sync; not throttled client-side.
- **Error codes** (Paycom's own usage, some non-standard): 200 OK, 201
  Created (PUT/POST), 202 Accepted (PATCH), 206 Partial Content
  (pagination), 400 Bad Request, 401 Unauthorized (**IP not allow-listed**
  - check this first if everything else looks right), 404 Not Found, 409
  Conflict, 413 Forbidden (**endpoint not enabled** for your API user, not
  the standard "payload too large" meaning), 429 Too Many Requests.
- **Webhooks are real and documented** - correcting an earlier version of
  this doc, which said webhook shape "isn't part of Paycom's public
  documentation." Paycom describes it as a "reverse API": you subscribe to
  change events (e.g. an employee status change), Paycom POSTs limited
  event details to a listener URL you host, and you call back into the
  API to retrieve the full record. The exact webhook payload schema is in
  a separate "Paycom Webhooks Guide" this session hasn't seen - ask your
  rep for it if you want to move off polling. See **Future: webhooks**
  below for how that would plug in.
- **Sensitive endpoints are disabled by default** and gated separately:
  Employee Sensitive, Employee Rates by Allocation, Employee Effective
  Rates by Allocation, Employee Taxes. Employee Sensitive includes SSN -
  this solution has no reason to request access to any of these four for
  an identity-provisioning use case.
- There's also a **change/audit-log endpoint** per employee
  (`GET api/v1/employee/{eecode}/change?startdate={unix}&enddate={unix}`,
  plus bulk variants surfaced in the API function-enablement UI as "Get
  Employee IDs Changes" and "Get Employee Change Fields") returning
  field-level diffs (`changedesc`, `changetime`, `old_value`, `new_value`).
  This solution doesn't use it yet - see **Future: delta sync** below.

## Wiring up your tenant's actual field names

Two things still need your tenant's real values, since the full field list
for `employeedirectory` lives in the separate, tenant-specific "Paycom
Endpoint Guide" this session hasn't seen:

1. **`PaycomClientOptions.CoreFieldAliases`** - only `EmployeeCode` ->
   `eecode` is confirmed; everything else (`WorkEmail`, `Department`,
   `Status`, ...) is still a placeholder guess. Replace with real field
   names from a sample response or your Endpoint Guide.
2. **`PaycomClientOptions.StatusValueMap`** - maps Paycom's raw status
   code/text to this solution's `EmploymentStatus` enum. Not confirmed by
   the API guide either; get the actual values `eestatus` returns.

Every field Paycom returns - not just the ones in `CoreFieldAliases` - lands
in `EmployeeRecord.RawFields` verbatim, so a `FieldMapping` in the admin UI
can reference any Paycom field directly by its native name.

## Validating against the real API

1. Get sandbox credentials from Paycom's automation team
   (`automation@paycomonline.com`, cc your Specialist) and confirm your
   region's base URL.
2. Call `employeedirectory` with a tool like Postman/Testfully first (as
   Paycom's own guide recommends) to see real field names before touching
   config.
3. Update `CoreFieldAliases` and `StatusValueMap` to match.
4. Run a **dry run** sync from the admin dashboard and review the
   per-employee preview before enabling the scheduled Function trigger.
5. Register the Function App's outbound IP(s) with Paycom (see IP
   allow-listing above) before the first live (non-sandbox) run.

## Future: delta sync

Right now every sync run pulls the full `employeedirectory` result set,
regardless of size - correctness relies on Entra's provisioning service
diffing the full record, not on us tracking deltas (see the main README/
architecture doc for why that's sufficient). For a very large employee
count, `GET employee/{eecode}/change` or its bulk "changed employee IDs"
variant could narrow each run to only employees that actually changed
since the last run, cutting bulkUpload volume - worth revisiting if run
time or Paycom call volume ever becomes a real constraint. It isn't one at
current documented rate limits.

## Future: webhooks

If your Paycom agreement includes webhook access, replacing (or
supplementing) the hourly timer with push notifications is possible
without changing the core pipeline: add an HTTP-triggered Azure Function
that validates the incoming webhook, then calls `SyncOrchestrator.RunAsync`
the same way `ManualSyncFunction` does. Implementing this needs the actual
webhook payload schema from Paycom's separate Webhooks Guide, which this
session doesn't have.
