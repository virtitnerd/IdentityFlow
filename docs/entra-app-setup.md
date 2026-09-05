# Entra ID setup

Three distinct Entra ID objects are involved. Set them up in this order.

## 1. The API-driven inbound provisioning job

This is the Microsoft-built Enterprise Application that receives the
`bulkUpload` calls and owns matching/attribute-mapping/lifecycle logic.

1. Entra admin center → **Enterprise applications** → **New application** →
   search the gallery for **"API-driven provisioning"** (Microsoft-owned
   template) → Create.
2. Open the new application → **Provisioning** → **Get started** → set
   provisioning mode to **Automatic**.
3. Under **Mappings**, configure **Provision Azure Active Directory Users**:
   - Set the matching attribute(s) - typically `userPrincipalName` and/or
     `employeeId`. At least one `FieldMapping` in this app must be flagged
     `IsMatchingAttribute = true` for the same target attribute.
   - Add target attributes for anything you'll map from Paycom: standard
     attributes (`displayName`, `department`, `jobTitle`, ...) plus any
     `extensionAttribute1`-`15` or directory schema extensions you want
     populated for dynamic group rules or other applications.
4. Save, then start provisioning. Note two values from this app's
   **Overview** / **Provisioning** pages for configuration:
   - **Service principal object ID** → `Entra:ProvisioningServicePrincipalId`
   - **Synchronization job ID** (visible in the provisioning job URL, or via
     `GET /servicePrincipals/{id}/synchronization/jobs`) → `Entra:ProvisioningJobId`

See Microsoft's own walkthrough for the click-by-click version:
https://learn.microsoft.com/entra/identity/app-provisioning/inbound-provisioning-api-configure-app

## 2. The sync service app registration (used by this solution)

This is a normal app registration your Function App and Web app authenticate
as (client credentials) to call Graph.

1. Entra admin center → **App registrations** → **New registration**.
   Name: `Paycom Entra Provisioner - Sync Service`. No redirect URI needed
   (this is a daemon app).
2. **Certificates & secrets** → create a client secret (or, preferably for
   production, upload a certificate and set `Entra:UseManagedIdentity` /
   swap to certificate auth - a client secret is the simplest path for a
   first deployment).
3. **API permissions** → **Add a permission** → **Microsoft Graph** →
   **Application permissions**, add:
   - `User.Read.All` (resolve users for group reconciliation)
   - `Group.ReadWrite.All` (assigned-group membership reconciliation)
4. Grant this app the provisioning job's own app role so it's allowed to
   call `bulkUpload`: on the **API-driven provisioning** service principal
   from step 1, grant this app registration the
   `SynchronizationData-User.Upload` app role. The cleanest way is via
   Microsoft Graph / PowerShell, since this role isn't exposed through the
   "Add a permission" picker the way standard Graph permissions are:

   ```powershell
   # Requires Microsoft.Graph PowerShell SDK, connected as a Global/Privileged Role Admin
   Connect-MgGraph -Scopes "AppRoleAssignment.ReadWrite.All"

   $syncApp = Get-MgServicePrincipal -Filter "displayName eq 'Paycom Entra Provisioner - Sync Service'"
   $provisioningApp = Get-MgServicePrincipal -Filter "displayName eq '<your API-driven provisioning app name>'"
   $role = $provisioningApp.AppRoles | Where-Object { $_.Value -eq 'SynchronizationData-User.Upload' }

   New-MgServicePrincipalAppRoleAssignment `
     -ServicePrincipalId $syncApp.Id `
     -PrincipalId $syncApp.Id `
     -ResourceId $provisioningApp.Id `
     -AppRoleId $role.Id
   ```
5. **Grant admin consent** for the Graph application permissions from step 3
   in the app registration's **API permissions** blade.
6. Record: **Application (client) ID** → `Entra:ClientId`, **Directory
   (tenant) ID** → `Entra:TenantId`, the client secret value →
   `Entra:ClientSecret`.

## 3. The admin UI sign-in app registration

Kept separate and minimally privileged since, unlike the sync service app,
this one is reachable from a browser.

1. **App registrations** → **New registration**. Name: `Paycom Entra
   Provisioner - Admin UI`. Redirect URI: `https://<your-web-app-host>/signin-oidc`
   (Web platform). Also add `https://localhost:{port}/signin-oidc` for local dev.
2. **API permissions**: delegated `User.Read` is sufficient (added by
   default).
3. **App roles** → create an app role named `ProvisionerAdmin` (matches
   `AzureAd:AdminAppRole` in appsettings), allowed member type **Users/Groups**.
4. **Enterprise applications** → find this app → **Users and groups** →
   assign the people/group who should have admin access, with the
   `ProvisionerAdmin` role. Anyone not assigned this role is denied by the
   Razor Pages app's fallback authorization policy even if they can sign in.
5. Record: **Application (client) ID** → `AzureAd:ClientId`, **Directory
   (tenant) ID** → `AzureAd:TenantId`. This app also needs a client secret
   if you keep the default authorization-code confidential-client flow -
   **Certificates & secrets** → new secret → `AzureAd:ClientSecret` (not
   currently read by `Program.cs`'s minimal config binding; add it to
   `AzureAd` config and to `AddMicrosoftIdentityWebApp` options if your
   tenant requires a confidential client, which is the default for a Web
   platform registration).

## Group assignment

For any Entra security group whose membership rule *can* be expressed as a
dynamic-membership rule (e.g. `user.department -eq "Sales"`), prefer that -
it needs no configuration in this solution at all, just an extension
attribute or core attribute mapped in step 1 above. Only fall back to a
`GroupAssignmentRule` in this app (Group Rules page) for group membership
that dynamic rules can't express, or that also carries manually managed
exception members.
