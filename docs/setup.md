# Setup and deployment

## Prerequisites

- .NET 10 SDK
- Azure Functions Core Tools v4 (`npm i -g azure-functions-core-tools@4`) for local Function debugging
- Azure CLI (`az`) with the Bicep extension (`az bicep install`)
- A SQL Server instance for local dev (LocalDB on Windows, or Azure SQL / SQL Server container elsewhere)
- The three Entra ID objects from [entra-app-setup.md](entra-app-setup.md)
- Paycom API access - see [paycom-integration.md](paycom-integration.md).
  Get sandbox credentials from Paycom's automation team first
  (`automation@paycomonline.com`) and allow-list your dev machine's IP
  before expecting local `GetAllEmployeesAsync` calls to succeed (a 401
  from Paycom almost always means the calling IP isn't allow-listed).

## Local development

1. `IdentityFlow.Functions.csproj` requires `local.settings.json`
   to exist just to build (Functions tooling copies it to the output
   directory) - copy the template and fill in your own values:
   ```
   cp src/IdentityFlow.Functions/local.settings.json.example src/IdentityFlow.Functions/local.settings.json
   ```
   Do the same conceptually for `src/IdentityFlow.Web/appsettings.Development.json`
   (no template checked in for it since ASP.NET Core doesn't require the
   file to exist - only add one if you want to override `appsettings.json`
   locally). Both need the same configuration keys - `Paycom:*`, `Entra:*`,
   `Sql:ConnectionString`, and (Web only) `AzureAd:*`. Never commit real
   secrets to either file; both are gitignored.
2. Apply the initial EF Core migration to your local database:
   ```
   dotnet tool install --global dotnet-ef
   dotnet ef database update --project src/IdentityFlow.Data --startup-project src/IdentityFlow.Data
   ```
   (Both hosts also call `Database.Migrate()` on startup, so this is mostly
   useful for inspecting the schema ahead of time.)
3. Run the Function host: `cd src/IdentityFlow.Functions && func start`
4. Run the Web app: `cd src/IdentityFlow.Web && dotnet run`
5. Add at least one `FieldMapping` with `IsMatchingAttribute = true`
   (typically `userPrincipalName`) before running a sync, or the Entra
   provisioning job has no way to anchor incoming records to existing users.
6. Use the dashboard's **Run Sync Now** with **Dry run** checked first, and
   review `SyncRuns/Details` before ever running it for real.

## Deploying the Azure resources

This template provisions **Azure SQL Database (single database),
GeneralPurpose tier, Serverless compute only** - `sqlDatabaseSkuName`
(default `GP_S_Gen5_1`) only picks the vCore size within that one tier.
Azure SQL Managed Instance, SQL Server on a VM, Elastic Pools, Hyperscale,
Business Critical, any DTU tier (Basic/Standard/Premium), and even
GeneralPurpose *Provisioned* (non-serverless) compute are not supported by
`infra/modules/sql.bicep` as written - it hardcodes `tier: 'GeneralPurpose'`
and the serverless-only `autoPauseDelay`/`minCapacity` properties, so
passing an incompatible SKU name fails the deployment. GeneralPurpose
Serverless auto-pauses when idle and bills accordingly, which fits this
workload's bursty, infrequent-sync-runs usage pattern - if you need a
different tier (e.g. Business Critical for higher availability SLAs),
`sql.bicep` needs updating to match, not just the parameter.

```
az group create -n rg-paycom-provisioner -l eastus2
az deployment group create \
  -g rg-paycom-provisioner \
  -f infra/main.bicep \
  -p infra/main.bicepparam
```

After the deployment finishes:

1. Populate the Key Vault secrets it created placeholders for (`Paycom-*`,
   `Entra-*`, `AzureAd-*` - see the `@Microsoft.KeyVault(...)` references in
   `infra/modules/functionApp.bicep` and `infra/modules/webApp.bicep` for
   the exact secret names expected):
   ```
   az keyvault secret set --vault-name <kv-name> --name Paycom-BaseUrl --value "https://..."
   az keyvault secret set --vault-name <kv-name> --name Entra-ClientSecret --value "..."
   # ...and so on for every @Microsoft.KeyVault(...) reference in the two app modules.
   ```
2. Run `infra/post-deploy-sql-grants.sql` against the new database as the
   Entra AAD admin configured in `main.bicepparam`, substituting the actual
   Function App / Web App names from the deployment output. This grants
   `db_ddladmin` to both managed identities specifically so the next step
   can work: neither Bicep/ARM nor this script create the actual application
   schema (tables, indexes, migrations history) - both hosts call
   `Database.Migrate()` on startup (see **Local development** above), so the
   schema gets created automatically the first time either app starts
   against the new database. No separate manual migration step against
   Azure SQL is needed, but the first startup after a fresh deployment will
   take a little longer than subsequent ones while it applies every
   migration from scratch.
3. Register the Function App's outbound IPs with Paycom before flipping on
   the live (non-sandbox) credentials - Paycom only accepts calls from
   allow-listed IPs:
   ```
   az functionapp show -g rg-paycom-provisioner -n <functionAppName> --query possibleOutboundIpAddresses -o tsv
   ```
   Send that list to your Paycom Specialist/automation team. See
   [paycom-integration.md](paycom-integration.md) for the VNet+NAT Gateway
   alternative if you'd rather have one static egress IP than a range.
4. Deploy the application code. Either build fresh from source:
   ```
   dotnet publish src/IdentityFlow.Functions -c Release -o out/functions
   func azure functionapp publish <functionAppName> --dotnet-isolated

   dotnet publish src/IdentityFlow.Web -c Release -o out/web
   az webapp deploy -g rg-paycom-provisioner -n <webAppName> --src-path out/web
   ```
   or, for a specific tagged version, download the pre-built artifacts the
   [release workflow](../.github/workflows/release.yml) attaches to every
   published GitHub Release (`IdentityFlow.Functions-<tag>.zip` /
   `IdentityFlow.Web-<tag>.zip`), unzip each, and run the same
   `func azure functionapp publish` / `az webapp deploy` commands above
   against the unzipped `out/functions` / `out/web` folders instead of a
   fresh local build - useful for deploying an exact, already-tested build
   rather than whatever happens to be checked out locally. That workflow is
   artifact-only today (it doesn't deploy anywhere itself) since there's no
   Azure target/credentials wired into CI yet - see its own comments for
   what a future auto-deploy-on-release step would still need.
5. Sign in to the Web app once as an admin and add your real field mappings
   and group rules (or seed them via a database script if you're migrating
   from an existing process).
6. Run a dry-run sync from the dashboard, review it, then let the Function's
   timer trigger take over.

## CI/CD

Two GitHub Actions workflows are checked in under `.github/workflows/`:

- **`ci.yml`** - on every push and pull request, restores, builds, and runs
  the full test suite in both Debug and Release configurations
  (matrixed, so a Release-only failure is caught and reported distinctly
  from a Debug one).
- **`release.yml`** - on every published GitHub Release, re-runs the test
  suite in Release, then `dotnet publish`es both hosts, zips each, and
  attaches them to the Release as downloadable assets. Artifact-only for
  now - see step 4 above for using those artifacts, and the workflow's own
  comments for what an actual auto-deploy-to-Azure step would still need
  (a real target subscription/resource group and an OIDC-based
  authentication setup, neither of which exist yet).
