param location string
param tags object
param sqlServerName string
param sqlDatabaseName string
param sqlDatabaseSkuName string
param aadAdminObjectId string
param aadAdminName string

// Entra-ID-only authentication - no SQL login/password is created for this
// server. Grant data access to each app's managed identity as a contained
// database user post-deploy (see docs/setup.md) since Bicep/ARM cannot run
// T-SQL directly.
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      login: aadAdminName
      sid: aadAdminObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: tags
  sku: {
    name: sqlDatabaseSkuName
    tier: 'GeneralPurpose'
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
  }
}

resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlServerName string = sqlServer.name
