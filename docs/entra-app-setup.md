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
   - **Set a matching attribute anchored on `employeeId`, not just `userPrincipalName`.**
     This is the actual mechanism that prevents Paycom's feed from ever
     creating a second, duplicate account for someone who already has one -
     it's Entra's provisioning job, not this app, that decides whether an
     incoming record is a create or an update to an existing user, and it
     decides that purely from whichever attribute(s) are configured here as
     matching. Matching on `userPrincipalName` alone is a real risk: if a
     worker's UPN ever changes (a legal name change, a typo fix, a domain
     migration) before this app's next sync reflects that, the provisioning
     job would see what looks like a brand-new person and create a second
     account instead of updating the existing one. `employeeId` doesn't
     change, so anchoring on it avoids that entirely.
   - This app already sends Paycom's employee code as the SCIM record's
     `externalId` on *every* record, unconditionally - it's not something a
     `FieldMapping` can omit or misconfigure. The simplest way to use it:
     add a mapping row here with source **`externalId`** and target
     **`employeeId`**, and flag it as a matching attribute. No `FieldMapping`
     in this app's own Field Mappings page is required for that specific
     row, since `externalId` is populated in code, not by admin
     configuration - though `employeeId` is also available as a Field
     Mapping target now if you'd rather map it explicitly from a Paycom
     field alongside `externalId` (see docs/architecture.md and the "Mapping
     any other Entra attribute" section below).
   - Keep `userPrincipalName` as a *second* matching attribute if you like
     (Entra supports more than one) - just don't rely on it alone as the
     only anchor. Whichever attribute(s) you use here, mark the
     corresponding `FieldMapping` row(s) in this app's Field Mappings page
     as `IsMatchingAttribute = true` too - that flag doesn't tell Entra
     anything (Entra's own matching config, set here, is entirely separate
     and is what actually prevents duplicate accounts), but it's what this
     app's own group-reconciliation and leaver-task logic uses internally to
     find the right existing Entra user via Graph, independent of the
     provisioning job.
   - Add target attributes for anything you'll map from Paycom: standard
     attributes (`displayName`, `department`, `jobTitle`, ...) plus any
     `extensionAttribute1`-`15` or directory schema extensions you want
     populated for dynamic group rules or other applications.
   - **Manager**: map a `FieldMapping` with `TargetAttribute = "manager"` from
     Paycom's manager-employee-code field (`ManagerEmployeeCode`). Send the
     manager's own Paycom employee code as the value, **not** their Entra
     object ID - Entra's provisioning service resolves this reference
     internally against the manager's own already-provisioned record
     (matched the same way as any other user, via `externalId`/`employeeId`),
     exactly like Microsoft's own reference samples do
     ([CSV2SCIM.ps1](https://github.com/AzureAD/entra-id-inbound-provisioning/tree/main/PowerShell/CSV2SCIM)'s
     `manager.value = 'ManagerID'`). Two consequences worth knowing: the
     manager must already exist as a provisioned/matched user for the
     reference to resolve - a brand-new employee reported in the same sync
     run as their brand-new manager may need one extra cycle before the
     link takes; and this app sends `manager` under the SCIM Enterprise User
     extension schema (not a bare top-level attribute), which is what makes
     it something Entra's Attribute Mapping page can actually recognize as a
     source in the first place.
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

Two important distinctions to keep straight here, since it's easy to
conflate them (an earlier version of this app's code did, briefly):

1. A **SCIM attribute name** - what `FieldMapping.TargetAttribute` actually
   means: the shape/path this app writes into the outgoing `bulkUpload`
   JSON. This is governed by the SCIM Core User (RFC 7643 §4.1) and
   Enterprise User extension (§4.3) schemas, not by whatever a directory
   attribute happens to be called.
2. An **Entra/Graph directory attribute name** (`employeeId`,
   `usageLocation`, `employeeHireDate`, ...) - what the incoming SCIM
   attribute eventually gets *written to*, decided entirely by the
   provisioning job's own **Attribute Mapping** configuration in the Entra
   admin center. This app has no influence over that decision beyond
   sending a value at all.

`MappingEngine` writes any target attribute name it doesn't explicitly
recognize (userPrincipalName, name, emails, addresses, etc. - the ones
needing a specific SCIM sub-object shape) as a flat top-level attribute.
That mechanism is only correct for attributes with no sub-object shape of
their own - which, for genuine SCIM attributes, means Core schema ones like
`userType`, `preferredLanguage`, `nickName`, `locale`, `timezone` (see
`KnownEntraAttributes.AdditionalWritableAttributes`). It is **not** correct
for a bare directory attribute name like `employeeHireDate` or
`usageLocation` - those have no SCIM representation at all, so typing them
as a target here produces a JSON key that sits outside every schema Entra's
provisioning job recognizes, and can't be attribute-mapped on the Entra
side no matter what.

To reach a directory attribute that has no SCIM equivalent, the provisioning
job's own schema has to be extended first, same as for a custom directory
extension:

1. Provisioning job → **Edit Provisioning** → **Mappings** → open the
   attribute mapping → **Advanced Options** → **Edit target User
   attributes**.
2. Add the attribute as a SCIM schema extension under your own namespace,
   e.g. `urn:ietf:params:scim:schemas:extension:contoso:1.0:User:HireDate`.
3. Add a new mapping from that extension attribute to the real target (e.g.
   `employeeHireDate`).
4. Only *then* does `urn:ietf:params:scim:schemas:extension:contoso:1.0:User:HireDate`
   become a meaningful `FieldMapping.TargetAttribute` value in this app - and
   because it's nested under a namespace object in the wire JSON (not a bare
   flat key), reaching it requires the same kind of dedicated nesting this
   app already gives `extensionAttribute1`-`15` and the Enterprise User
   attributes, not the generic flat-attribute fallback.

See Microsoft's own worked example (HireDate/JobCode via a custom `contoso`
namespace):
https://learn.microsoft.com/entra/identity/app-provisioning/inbound-provisioning-api-custom-attributes

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
