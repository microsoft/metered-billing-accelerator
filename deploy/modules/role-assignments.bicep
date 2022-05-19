param eventHubName string
param storageName string

param principalId string
param appNamePrefix string
param storageRole string = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe' // Storage Blob Data Contributor role
param evenHubRole string = 'f526a384-b230-433a-b45c-95f59c4a2dec' // Azure Event Hubs Data Owner role

resource eventHub 'Microsoft.EventHub/namespaces/eventhubs@2021-11-01' existing = {
  name: eventHubName
}

resource eventhubRoleAssignment 'Microsoft.Authorization/roleAssignments@2020-08-01-preview' = {
  name: guid(appNamePrefix, eventHubName)

  scope: eventHub
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', evenHubRole)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2021-08-01' existing = {
  name: storageName
}

resource storageRoleAssignment 'Microsoft.Authorization/roleAssignments@2020-08-01-preview' = {
  name: guid(appNamePrefix, storageName)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageRole)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
