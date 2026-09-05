# Paycom → Entra ID Provisioner

An in-house solution that replaces manual "get a new-hire email from HR,
go create the Entra ID account by hand" with an automated pipeline: pull
current worker data from Paycom, map it onto Entra ID users (including
extension attributes), submit it to Entra's own **API-driven inbound
provisioning** service (which handles create/update/disable), and reconcile
assigned-group membership - all on a schedule, with a web UI for
configuration, dry-run previews, and run history.

See [docs/architecture.md](docs/architecture.md) for the full design and why
it's built this way instead of a hand-rolled SCIM server.

## Solution layout

```
src/
  PaycomEntraProvisioner.Core/      Domain models, mapping engine, group rule
                                     evaluator, SyncOrchestrator (shared pipeline)
  PaycomEntraProvisioner.Paycom/    Configurable Paycom API client
  PaycomEntraProvisioner.Graph/     Entra bulkUpload client + Graph directory/group client
  PaycomEntraProvisioner.Data/      EF Core persistence (Azure SQL)
  PaycomEntraProvisioner.Functions/ Azure Functions isolated worker (timer + HTTP triggers)
  PaycomEntraProvisioner.Web/       Razor Pages admin/monitoring UI (Entra ID sign-in)
tests/
  PaycomEntraProvisioner.Core.Tests/
infra/                              Bicep IaC
docs/                               Architecture, Entra app setup, Paycom integration, deployment
```

Built on .NET 10 / C#.

## Getting started

1. Read [docs/entra-app-setup.md](docs/entra-app-setup.md) and set up the
   three Entra ID objects this solution needs (the API-driven provisioning
   job, the sync service app registration, the admin UI sign-in app
   registration).
2. Read [docs/paycom-integration.md](docs/paycom-integration.md) - Paycom
   access and field layout are negotiated per customer, so this needs a
   short config pass against your tenant's actual API contract.
3. Follow [docs/setup.md](docs/setup.md) for local development and Azure
   deployment (Bicep in `infra/`).

## Status

This is a working scaffold: the full pipeline (Paycom pull → field mapping
→ SCIM bulk submission → group reconciliation → run history) builds, has
unit test coverage on the mapping/rule engines, and is ready to wire up
against a real Paycom API contract and a real Entra tenant. It has not been
run against live Paycom or Entra ID services in this environment - validate
with dry runs before enabling the scheduled trigger in production.
