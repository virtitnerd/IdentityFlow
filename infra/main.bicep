// IdentityFlow (Paycom -> Entra ID provisioning) - main deployment
//
// Provisions: Log Analytics + Application Insights, a Key Vault, an Azure
// SQL Database, a Linux Function App (the sync engine) and a Linux Web App
// (the Razor Pages admin/monitoring UI), each with a system-assigned
// managed identity and Key Vault access via RBAC.
//
// Secrets (Paycom credentials, the Entra sync app's client secret, and the
// SQL connection string) are stored in Key Vault and wired into each app's
// settings via Key Vault references (@Microsoft.KeyVault(...)) so no
// secret value ever needs to live in this template or in source control.
//
// Validate before first deploy:
//   az bicep build --file main.bicep
//   az deployment group what-if -g <rg> -f main.bicep -p @main.bicepparam
targetScope = 'resourceGroup'

@description('Short, unique name used as the base for all resource names (e.g. "identityflow"). Lowercase alphanumeric, 3-15 chars.')
@minLength(3)
@maxLength(15)
param appName string

@description('Deployment environment tag/suffix, e.g. dev, test, prod.')
param environmentName string = 'prod'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Entra object ID of the user or group to set as the Azure SQL Active Directory administrator.')
param sqlAadAdminObjectId string

@description('Display name of the Azure SQL AAD admin (user or group name).')
param sqlAadAdminName string

@description('vCore size of the Azure SQL Database, GeneralPurpose Serverless tier only (name format GP_S_Gen5_<vCores>, e.g. GP_S_Gen5_1, GP_S_Gen5_2, GP_S_Gen5_4). This template only supports that one tier - sql.bicep hardcodes tier: \'GeneralPurpose\' and sets the serverless-only autoPauseDelay/minCapacity properties unconditionally, so a Hyperscale, Business Critical, DTU-tier (Basic/Standard/Premium), or even non-serverless GeneralPurpose SKU name will fail deployment. Azure SQL Managed Instance and SQL Server on a VM are not supported by this template at all.')
param sqlDatabaseSkuName string = 'GP_S_Gen5_1'

@description('App Service Plan SKU for the Razor Pages admin UI.')
param webAppSkuName string = 'B1'

@description('Tags applied to every resource.')
param tags object = {
  application: 'identityflow'
  environment: environmentName
}

var resourceToken = '${appName}${environmentName}'
var functionAppName = 'func-${resourceToken}'
var webAppName = 'app-${resourceToken}'
var keyVaultName = take('kv-${resourceToken}', 24)
var sqlServerName = 'sql-${resourceToken}'
var sqlDatabaseName = 'IdentityFlow'
var storageAccountName = take(toLower(replace('st${resourceToken}', '-', '')), 24)
var logAnalyticsName = 'log-${resourceToken}'
var appInsightsName = 'appi-${resourceToken}'
var functionPlanName = 'plan-func-${resourceToken}'
var webPlanName = 'plan-web-${resourceToken}'

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    tags: tags
    logAnalyticsName: logAnalyticsName
    appInsightsName: appInsightsName
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    tags: tags
    storageAccountName: storageAccountName
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyVault'
  params: {
    location: location
    tags: tags
    keyVaultName: keyVaultName
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    tags: tags
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDatabaseName
    sqlDatabaseSkuName: sqlDatabaseSkuName
    aadAdminObjectId: sqlAadAdminObjectId
    aadAdminName: sqlAadAdminName
  }
}

module functionApp 'modules/functionApp.bicep' = {
  name: 'functionApp'
  params: {
    location: location
    tags: tags
    functionAppName: functionAppName
    functionPlanName: functionPlanName
    storageAccountName: storage.outputs.storageAccountName
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    sqlServerFqdn: sql.outputs.sqlServerFqdn
    sqlDatabaseName: sqlDatabaseName
  }
}

module webApp 'modules/webApp.bicep' = {
  name: 'webApp'
  params: {
    location: location
    tags: tags
    webAppName: webAppName
    webPlanName: webPlanName
    webAppSkuName: webAppSkuName
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    sqlServerFqdn: sql.outputs.sqlServerFqdn
    sqlDatabaseName: sqlDatabaseName
  }
}

// Grant both apps' managed identities access to read Key Vault secrets.
module keyVaultAccess 'modules/keyvaultAccess.bicep' = {
  name: 'keyVaultAccess'
  params: {
    keyVaultName: keyVault.outputs.keyVaultName
    principalIds: [
      functionApp.outputs.principalId
      webApp.outputs.principalId
    ]
  }
}

output functionAppName string = functionApp.outputs.functionAppName
output functionAppHostName string = functionApp.outputs.defaultHostName
output webAppName string = webApp.outputs.webAppName
output webAppHostName string = webApp.outputs.defaultHostName
output keyVaultName string = keyVault.outputs.keyVaultName
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output functionAppPrincipalId string = functionApp.outputs.principalId
output webAppPrincipalId string = webApp.outputs.principalId
