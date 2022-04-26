
param eventHubNameNamespaceName string = 'marketplace-saas-metering-standard'
param storageAccountName string = 'meteringmgjoshevski'
param infraServicePrincipalObjectID string = 'f5b5d938-ff1f-437f-b18b-cea64df21365'
param infraServicePrincipalObjectType string = 'ServicePrincipal'
param prefix string = '${utcNow('yyyy-MM-dd')}-mgjoshevski'
param location string = resourceGroup().location

var names = {
  containers: {
    capture: '${prefix}-capture'
    snapshots: '${prefix}-snapshots'
    checkpoint: '${prefix}-checkpoint'
  }
  roleDefinitions: {
    evntHub: {
      receiver: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a638d3c7-ab3a-418d-83e6-5f17a39d4fde')
      sender: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '2b629674-e913-4c01-ae53-ef4638d8f975')
    }
    blob: {
      contributer: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    }
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2021-08-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_RAGRS'
  }
  properties: {
    allowBlobPublicAccess: false
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    isHnsEnabled: true
  }
}

resource captureContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-08-01' = {
  name: '${storageAccount.name}/default/${names.containers.capture}'
}
resource checkpointContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-08-01' = {
  name: '${storageAccount.name}/default/${names.containers.checkpoint}'
}
resource snapshotsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-08-01' = {
  name: '${storageAccount.name}/default/${names.containers.snapshots}'
}

resource captureBlobContributorRole 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid(infraServicePrincipalObjectID, captureContainer.name, prefix, 'captureBlobContributorRole')
  scope: captureContainer
  properties: {
    roleDefinitionId: names.roleDefinitions.blob.contributer
    principalId: infraServicePrincipalObjectID
    principalType: infraServicePrincipalObjectType
  }
}
resource checkpointBloContributorRole 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid(infraServicePrincipalObjectID, checkpointContainer.name, prefix, 'checkpointBloContributorRole')
  scope: checkpointContainer
  properties: {
    roleDefinitionId: names.roleDefinitions.blob.contributer
    principalId: infraServicePrincipalObjectID
    principalType: infraServicePrincipalObjectType
  }
}
resource snapshotsBlobContributorRole 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid(infraServicePrincipalObjectID, snapshotsContainer.name, prefix, 'snapshotsBlobContributorRole')
  scope: snapshotsContainer
  properties: {
    roleDefinitionId: names.roleDefinitions.blob.contributer
    principalId: infraServicePrincipalObjectID
    principalType: infraServicePrincipalObjectType
  }
}

resource eventHubNamespace 'Microsoft.EventHub/namespaces@2021-11-01' = {
  name: eventHubNameNamespaceName
  location: location
  sku: {
    capacity: 1
    name: 'Standard'
    tier: 'Standard'
  }  
  properties: {
    disableLocalAuth: false
    isAutoInflateEnabled: false
    kafkaEnabled: true
    maximumThroughputUnits: 0
    zoneRedundant: true
  }
}

resource eventHub 'Microsoft.EventHub/namespaces/eventhubs@2021-11-01' = {
  name: '${eventHubNamespace.name}/${prefix}'
  properties: {
    partitionCount: 1
    messageRetentionInDays: 2
    captureDescription: {
      enabled: true
      skipEmptyArchives: true
      encoding: 'Avro'
      intervalInSeconds: 15 * 60
      sizeLimitInBytes: 300 * 1024 * 1024
      destination: {
        name: 'EventHubArchive.AzureBlockBlob'
        properties: {
          archiveNameFormat: '{Namespace}/{EventHub}/p{PartitionId}--{Year}-{Month}-{Day}--{Hour}-{Minute}-{Second}'
          storageAccountResourceId: storageAccount.id
          blobContainer: names.containers.capture
        }
      }
    }
  }
}

resource azureEventHubsDataSenderRole 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid(infraServicePrincipalObjectID, eventHubNamespace.id, prefix, 'azureEventHubsDataSenderRole')
  scope: eventHub
  properties: {
    roleDefinitionId: names.roleDefinitions.evntHub.sender
    principalId: infraServicePrincipalObjectID
    principalType: infraServicePrincipalObjectType
  }
}
resource ehReceiverRoleAssignment 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid(infraServicePrincipalObjectID, eventHubNamespace.id, prefix, 'ehReceiverRoleAssignment')
  scope: eventHub
  properties: {
    roleDefinitionId: names.roleDefinitions.evntHub.receiver
    principalId: infraServicePrincipalObjectID
    principalType: infraServicePrincipalObjectType
  }
}

output environmentConfiguration object = {
  checkpointContainer: 'https://${substring(checkpointContainer.name, 0, indexOf(checkpointContainer.name, '/'))}.blob.${environment().suffixes.storage}/${substring(checkpointContainer.name, lastIndexOf(checkpointContainer.name, '/') + 1)}'
  snapshotsContainer: 'https://${substring(snapshotsContainer.name, 0, indexOf(snapshotsContainer.name, '/'))}.blob.${environment().suffixes.storage}/${substring(snapshotsContainer.name, lastIndexOf(snapshotsContainer.name, '/') + 1)}' 
  captureContainer: 'https://${substring(captureContainer.name, 0, indexOf(captureContainer.name, '/'))}.blob.${environment().suffixes.storage}/${substring(captureContainer.name, lastIndexOf(captureContainer.name, '/') + 1)}'
  captureFormat: eventHub.properties.captureDescription.destination.properties.archiveNameFormat
  eventHubNamespaceName: eventHubNamespace.name
  infraServicePrincipal: infraServicePrincipalObjectID
  eventHub: eventHub.name
}
