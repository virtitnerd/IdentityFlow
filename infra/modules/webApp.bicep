param location string
param tags object
param webAppName string
param webPlanName string
param webAppSkuName string
param appInsightsConnectionString string
param keyVaultName string
param sqlServerFqdn string
param sqlDatabaseName string

resource webPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: webPlanName
  location: location
  tags: tags
  sku: {
    name: webAppSkuName
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: webPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      alwaysOn: true
      appSettings: [
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        { name: 'Sql__ConnectionString', value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;' }
        { name: 'AzureAd__Instance', value: 'https://login.microsoftonline.com/' }
        { name: 'AzureAd__TenantId', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=AzureAd-TenantId)' }
        { name: 'AzureAd__ClientId', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=AzureAd-ClientId)' }
        { name: 'AzureAd__ClientSecret', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=AzureAd-ClientSecret)' }
        { name: 'Paycom__BaseUrl', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Paycom-BaseUrl)' }
        { name: 'Paycom__AuthMode', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Paycom-AuthMode)' }
        { name: 'Paycom__Sid', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Paycom-Sid)' }
        { name: 'Paycom__ApiToken', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Paycom-ApiToken)' }
        { name: 'Entra__TenantId', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Entra-TenantId)' }
        { name: 'Entra__ClientId', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Entra-ClientId)' }
        { name: 'Entra__ClientSecret', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Entra-ClientSecret)' }
        { name: 'Entra__ProvisioningServicePrincipalId', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Entra-ProvisioningServicePrincipalId)' }
        { name: 'Entra__ProvisioningJobId', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Entra-ProvisioningJobId)' }
      ]
    }
  }
}

output webAppName string = webApp.name
output defaultHostName string = webApp.properties.defaultHostName
output principalId string = webApp.identity.principalId
