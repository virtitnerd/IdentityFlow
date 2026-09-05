param location string
param tags object
param functionAppName string
param functionPlanName string
param storageAccountName string
param appInsightsConnectionString string
param keyVaultName string
param sqlServerFqdn string
param sqlDatabaseName string

@description('NCRONTAB schedule for the sync timer trigger (6 fields, seconds first). Default: hourly on the hour.')
param syncSchedule string = '0 0 * * * *'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource functionPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: functionPlanName
  location: location
  tags: tags
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  kind: 'functionapp'
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: functionPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|10.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage', value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'WEBSITE_RUN_FROM_PACKAGE', value: '1' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        { name: 'SyncSchedule', value: syncSchedule }
        { name: 'Sql__ConnectionString', value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;' }
        { name: 'Paycom__BaseUrl', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Paycom-BaseUrl)' }
        { name: 'Paycom__AuthMode', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Paycom-AuthMode)' }
        { name: 'Paycom__Sid', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Paycom-Sid)' }
        { name: 'Paycom__ApiToken', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Paycom-ApiToken)' }
        { name: 'Entra__TenantId', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Entra-TenantId)' }
        { name: 'Entra__ClientId', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Entra-ClientId)' }
        { name: 'Entra__ClientSecret', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Entra-ClientSecret)' }
        { name: 'Entra__ProvisioningServicePrincipalId', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Entra-ProvisioningServicePrincipalId)' }
        { name: 'Entra__ProvisioningJobId', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Entra-ProvisioningJobId)' }
        { name: 'Entra__SyncServiceAppObjectId', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Entra-SyncServiceAppObjectId)' }
      ]
    }
  }
}

output functionAppName string = functionApp.name
output defaultHostName string = functionApp.properties.defaultHostName
output principalId string = functionApp.identity.principalId
