-- Run once after the first deployment, connected to the IdentityFlow
-- database as the Entra ID AAD admin configured in main.bicep (e.g. via
-- `sqlcmd` with Active Directory auth, or the Azure Portal's Query Editor).
--
-- Bicep/ARM cannot create T-SQL contained database users directly, so the
-- Function App's and Web App's managed identities are granted access here.
-- Replace the two names below with the exact Function App / Web App names
-- from the deployment outputs (functionAppName / webAppName) - for a
-- system-assigned identity, the contained user name must match the app's
-- name exactly.

CREATE USER [func-paycomprov-prod] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [func-paycomprov-prod];
ALTER ROLE db_datawriter ADD MEMBER [func-paycomprov-prod];
ALTER ROLE db_ddladmin ADD MEMBER [func-paycomprov-prod]; -- needed for EF Core migrations run at startup

CREATE USER [app-paycomprov-prod] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [app-paycomprov-prod];
ALTER ROLE db_datawriter ADD MEMBER [app-paycomprov-prod];
ALTER ROLE db_ddladmin ADD MEMBER [app-paycomprov-prod];
