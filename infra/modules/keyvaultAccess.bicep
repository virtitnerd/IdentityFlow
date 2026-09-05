param keyVaultName string
param principalIds array

// Key Vault Secrets User
var secretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource roleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in principalIds: {
    name: guid(keyVault.id, principalId, secretsUserRoleId)
    scope: keyVault
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', secretsUserRoleId)
      principalId: principalId
      principalType: 'ServicePrincipal'
    }
  }
]
