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
   - `User.ReadWrite.All` (resolving users for group reconciliation only
     needs read access, but the Lifecycle Policy Engine's `RevokeSignInSessions`
     and `DeleteAccount` leaver tasks call `POST /users/{id}/revokeSignInSessions`
     and `DELETE /users/{id}`, both of which require write. If your org's
     leaver policy only ever removes group memberships and never revokes
     sessions or deletes accounts, `User.Read.All` is sufficient instead -
     leave those two task types disabled in the Admin Portal's Lifecycle
     Policy page in that case.)
   - `Group.ReadWrite.All` (assigned-group membership reconciliation and the
     `RemoveFromAllAssignedGroups` leaver task)
   - `Application.Read.All` (optional - lets the admin UI list this app's
     own registered custom directory extension attributes as target-field
     suggestions; the Field Mappings page works without it, just without
     that one suggestion source)
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
   `Entra:ClientSecret`, and the **Object ID** (also on the Overview page -
   distinct from the Application/client ID) → `Entra:SyncServiceAppObjectId`.

### Mapping any other Entra attribute

The Field Mappings page's target attribute isn't limited to the handful of
attributes suggested by autocomplete. `MappingEngine` writes any target
attribute name it doesn't explicitly recognize (userPrincipalName, name,
emails, manager, addresses, etc. - the ones needing a specific SCIM
sub-object shape) as a flat top-level attribute instead, so any standard
Microsoft Entra ID / Azure AD Connect provisioning-schema attribute -
`employeeId`, `employeeType`, `employeeHireDate`, `usageLocation`,
`preferredLanguage`, and others - can be targeted by typing its name, even
before it's added to any suggestion list.

That said, this app being *willing* to send an attribute doesn't mean
Entra's provisioning job will *persist* it - the job's own **Attribute
Mapping** decides what an incoming record is allowed to write, exactly the
same requirement already noted below for custom directory extensions.
Before relying on a mapping to a less-common attribute, confirm it's
listed under **Attribute Mapping → Advanced Options → Edit target User
attributes** on the provisioning job from step 1, and add it there if not.

`employeeLeaveDateTime` is worth calling out specifically: Microsoft treats
it as a sensitive attribute requiring an extra one-time consent/role grant
beyond the standard Graph application permissions above before any app can
write to it. Verify current Microsoft Learn guidance for the exact grant
required before mapping to it.

### Optional: custom directory extension attributes

If you want a Paycom field to land somewhere other than a core attribute
or one of the 15 `extensionAttribute` slots - your own named property for
a dynamic group rule or another application to read - register it as a
directory extension on this same app registration. There's no Entra admin
center UI for this; it's PowerShell/Graph-only:

```powershell
Connect-MgGraph -Scopes "Application.ReadWrite.All"

$syncApp = Get-MgApplication -Filter "displayName eq 'Paycom Entra Provisioner - Sync Service'"

New-MgApplicationExtensionProperty -ApplicationId $syncApp.Id -BodyParameter @{
    name = "CostCenter"
    dataType = "String"
    targetObjects = @("User")
}
```

That creates an attribute named `extension_<appId-without-hyphens>_CostCenter`
on every user. Once created, it shows up automatically as a target-attribute
suggestion on the Field Mappings page (via `Application.Read.All` - see step
3 above) - no restart needed, the page reads it live from Graph on each load.
You'll also need to add it manually under the API-driven provisioning job's
**Attribute Mapping → Advanced Options → Edit target User attributes** (see
[paycom-integration.md](paycom-integration.md) and Microsoft's own inbound
provisioning docs) before it'll actually be written by `bulkUpload`.

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
