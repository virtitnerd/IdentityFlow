# Setup and deployment

## Prerequisites

- .NET 10 SDK
- Azure Functions Core Tools v4 (`npm i -g azure-functions-core-tools@4`) for local Function debugging
- Azure CLI (`az`) with the Bicep extension (`az bicep install`)
- A SQL Server instance for local dev (LocalDB on Windows, or Azure SQL / SQL Server container elsewhere)
- The three Entra ID objects from [entra-app-setup.md](entra-app-setup.md)
- Paycom API access - see [paycom-integration.md](paycom-integration.md)

## Local development

1. `src/PaycomEntraProvisioner.Functions/local.settings.json` and
   `src/PaycomEntraProvisioner.Web/appsettings.Development.json` both need
   the same configuration keys - `Paycom:*`, `Entra:*`, `Sql:ConnectionString`,
   and (Web only) `AzureAd:*`. Never commit real secrets to either file;
   `local.settings.json` is already gitignored.
2. Apply the initial EF Core migration to your local database:
   ```
   dotnet tool install --global dotnet-ef
   dotnet ef database update --project src/PaycomEntraProvisioner.Data --startup-project src/PaycomEntraProvisioner.Data
   ```
   (Both hosts also call `Database.Migrate()` on startup, so this is mostly
   useful for inspecting the schema ahead of time.)
3. Run the Function host: `cd src/PaycomEntraProvisioner.Functions && func start`
4. Run the Web app: `cd src/PaycomEntraProvisioner.Web && dotnet run`
5. Add at least one `FieldMapping` with `IsMatchingAttribute = true`
   (typically `userPrincipalName`) before running a sync, or the Entra
   provisioning job has no way to anchor incoming records to existing users.
6. Use the dashboard's **Run Sync Now** with **Dry run** checked first, and
   review `SyncRuns/Details` before ever running it for real.

## Deploying the Azure resources

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
   Function App / Web App names from the deployment output.
3. Deploy the application code:
   ```
   dotnet publish src/PaycomEntraProvisioner.Functions -c Release -o out/functions
   func azure functionapp publish <functionAppName> --dotnet-isolated

   dotnet publish src/PaycomEntraProvisioner.Web -c Release -o out/web
   az webapp deploy -g rg-paycom-provisioner -n <webAppName> --src-path out/web
   ```
4. Sign in to the Web app once as an admin and add your real field mappings
   and group rules (or seed them via a database script if you're migrating
   from an existing process).
5. Run a dry-run sync from the dashboard, review it, then let the Function's
   timer trigger take over.

## CI

There's no CI workflow checked in yet. At minimum, wire up a GitHub Actions
workflow that runs `dotnet build` and `dotnet test` on the solution
(`PaycomEntraProvisioner.slnx`) on every PR before this goes to a shared
branch.
