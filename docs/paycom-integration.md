# Paycom integration notes

Paycom does not publish a public developer portal - access is provisioned
per customer by a Paycom representative. The facts below are confirmed from
Paycom's own **"API Companion Guide" (2026)** provided by a Paycom
customer, cross-checked against two further primary-source Paycom PDFs
(a "Client API Checklist" and an "Automation: Getting Started Guide") and
a real open-source Paycom connector implementation (see **Prior art**
below) found in a follow-up research pass - not guessed from third-party
aggregator sites. Where something is still unconfirmed for your specific
tenant, it's called out explicitly.

**A note on sources to distrust**: several SEO/aggregator sites
(getknit.dev, Rollout.com, ApiX-Drive) publish confident-sounding "Paycom
API" documentation that is almost certainly generic templated content, not
real - it describes a fictional `api.paycom.com` domain, OAuth2 bearer
auth, and a generic `/employees`/`/payrolls`/`/time_entries` CRUD surface
that matches none of the confirmed facts below. Don't cite them.

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
- **A sandbox/demo environment is real and confirmed**: Paycom's own
  "Automation: Getting Started Guide" describes "Demo API Accounts" - test
  credentials against a test Paycom account with fictional data, obtained
  by asking your representative (not self-service). Ask for these before
  touching a live tenant.
- **The onboarding flow, confirmed from a second primary-source PDF**:
  discovery call with your Paycom rep -> NDA (sometimes required before
  any docs are shared) -> a proposal/MSA to add API access to your Paycom
  suite -> SID/Token issuance -> a first test call against the base URL ->
  then endpoint-by-endpoint script building. The live, current, tenant-
  specific endpoint documentation lives inside the product itself (**User
  Options -> User Access and Security -> API Setup -> Documentation**,
  exportable to PDF) rather than as a single static public spec - this is
  why "Companion Guide" PDFs circulating among different customers can
  differ by year and by tenant.
- **Multiple legal entities (EINs) under one Paycom instance is a real
  gotcha**, per a working Paycom-to-Entra ID provisioning vendor
  (Joinly): the same person can exist across multiple EINs, and
  termination logic needs to trigger only once *all* of a person's active
  employments end, not on the first one. If your organization has more
  than one EIN in Paycom, validate whether `employeedirectory` can return
  more than one row per person (one per EIN) before assuming `eecode` is
  a stable 1:1 key - this solution currently assumes one row per employee.

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

## Prior art: a real open-source Paycom connector

A follow-up research pass found an actual open-source Paycom connector -
[Tools4everBV/HelloID-Conn-Prov-Source-Paycom](https://github.com/Tools4everBV/HelloID-Conn-Prov-Source-Paycom)
(PowerShell, for the commercial HelloID IAM platform) - which independently
confirms the base URL and Basic-auth scheme above, but uses a **different
roster call than this solution assumes**: `GET api/v1/employeeid` for the
employee ID list, then a separate `GET api/v1/employee/{eecode}` call
*per employee* for details, plus `GET api/v1/employee/{eecode}/customfield`
*per employee* for custom fields (returned as an array of
`{description, value}` pairs, not flat properties).

This doesn't necessarily mean `employeedirectory` is wrong - Paycom likely
exposes more than one way to enumerate employees - but it's worth
confirming during setup whether your tenant's `employeedirectory` response
actually contains the full field set you need, or whether you'll also need
per-employee detail/custom-field calls. If the latter, `PaycomHttpClient`
will need a second, per-employee fetch stage - flagged here rather than
assumed away.

**Worth avoiding, seen in that reference implementation**: it calls
per-employee endpoints one at a time with no concurrency, fetches only the
roster's first page with no continuation loop, and has no retry/backoff at
all (masked with a 3600-second timeout instead). If a per-employee detail
call ends up being necessary here, budget for the same batching/retry
care already built into `PaycomHttpClient`'s pagination and
`EntraProvisioningClient`'s throttling - don't repeat that anti-pattern.

## Validating against the real API

1. Get sandbox credentials from Paycom's automation team
   (`automation@paycomonline.com`, cc your Specialist) and confirm your
   region's base URL.
2. Call `employeedirectory` with a tool like Postman/Testfully first (as
   Paycom's own guide recommends) to see real field names before touching
   config. While there, check:
   - Whether it returns the full field set you need, or only IDs/basics
     (see **Prior art** above - you may need per-employee detail calls too).
   - **Whether terminated employees come back at all without an explicit
     `eestatus` filter.** This is genuinely unconfirmed even after
     dedicated research - Paycom's data model never deletes a terminated
     employee's record, but that doesn't guarantee the *default* directory
     listing includes them. If it doesn't, the "vanished from feed" safety
     net in `SyncOrchestrator` (see the architecture doc) stops being a
     rare edge case and becomes the **primary** way terminations are
     detected in practice - worth knowing going in rather than discovering
     it the first time someone leaves the company.
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
