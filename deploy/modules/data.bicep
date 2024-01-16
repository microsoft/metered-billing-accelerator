targetScope = 'resourceGroup'

param appNamePrefix string

param location string = resourceGroup().location

// Storage
param isHnsEnabled bool = false

// EventHub
@allowed([ 'Standard', 'Basic' ])
param eventHubSku string = 'Standard'

param skuCapacity int = 1

param archiveNameFormat string = '{Namespace}/{EventHub}/p={PartitionId}/y={Year}/m={Month}/d={Day}/h={Hour}/mm={Minute}/{Second}'

param messageRetentionInDays int = 3

param partitionCount int = 31

@description('The object ID of the group or service principal submitting usage events')
param senderObjectId string

@description('Optional object ID of service principal for metered-billing accelerator infra access. If you do not specify this, we create a UAMI')
param AZURE_METERING_INFRA_CLIENT_ID string = ''

var config = {
  eventHub: {    
    partitionCount: partitionCount
    skuCapacity: skuCapacity
    sku: eventHubSku
    messageRetentionInDays: messageRetentionInDays
    capture: {
      archiveNameFormat: archiveNameFormat
      intervalInSeconds: 300
      captureWindowSizeInMB: 300
    }
  }
}

var names = {
  identity: '${appNamePrefix}-metered-billing-accelerator'
  storage: appNamePrefix
  containers: {
    capture: '${appNamePrefix}-capture'
    snapshots: '${appNamePrefix}-snapshots'
    checkpoint: '${appNamePrefix}-checkpoint'
  }
  eventHub: {
    namespaceName: appNamePrefix
    hubName: 'metering'
  }
  roleDefinitions: {
    eventHub: {
      receiver:  'a638d3c7-ab3a-418d-83e6-5f17a39d4fde'
      sender:    '2b629674-e913-4c01-ae53-ef4638d8f975'
      dataOwner: 'f526a384-b230-433a-b45c-95f59c4a2dec'
    }
    blob: {
      contributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
    }
  }
}

resource captureAndStateStorageAccount 'Microsoft.Storage/storageAccounts@2022-05-01' = {
  name: names.storage
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_RAGRS' }
  tags: {
    prefix: appNamePrefix
  }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    defaultToOAuthAuthentication: true
    isHnsEnabled: isHnsEnabled
  }
}

resource captureContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2022-05-01' = {
  name: '${captureAndStateStorageAccount.name}/default/${names.containers.capture}'
}

resource snapshotsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2022-05-01' = {
  name: '${captureAndStateStorageAccount.name}/default/${names.containers.snapshots}'
}

resource checkpointContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2022-05-01' = {
  name: '${captureAndStateStorageAccount.name}/default/${names.containers.checkpoint}'
}

resource eh_namespace 'Microsoft.EventHub/namespaces@2021-11-01' = {
  name: names.eventHub.namespaceName
  location: location
  tags: {
    prefix: appNamePrefix
  }
  sku: {
    name: config.eventHub.sku
    tier: config.eventHub.sku
    capacity: config.eventHub.skuCapacity
  }
  properties: {
    disableLocalAuth: false
    isAutoInflateEnabled: false
    kafkaEnabled: true
    maximumThroughputUnits: 0
  }
}

resource eventHub 'Microsoft.EventHub/namespaces/eventhubs@2021-11-01' = {
  parent: eh_namespace
  name: names.eventHub.hubName
  properties: {
    partitionCount: config.eventHub.partitionCount
    messageRetentionInDays: config.eventHub.messageRetentionInDays
    captureDescription: {
      enabled: true
      encoding: 'Avro'
      intervalInSeconds: config.eventHub.capture.intervalInSeconds
      sizeLimitInBytes: config.eventHub.capture.captureWindowSizeInMB * 1024 * 1024
      skipEmptyArchives: true
      destination: {
        name: 'EventHubArchive.AzureBlockBlob'
        properties: {
          storageAccountResourceId: captureAndStateStorageAccount.id
          archiveNameFormat: config.eventHub.capture.archiveNameFormat
          blobContainer: names.containers.capture
        }
      }
    }
  }
}

resource eventhubRoleAssignmentApplications 'Microsoft.Authorization/roleAssignments@2020-08-01-preview' = {
  name: guid(senderObjectId, eventHub.name, names.roleDefinitions.eventHub.sender)
  scope: eventHub
  properties: {
    description: '${senderObjectId} should be a Azure Event Hubs Sender on ${eventHub.id}'
    principalId: senderObjectId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', names.roleDefinitions.eventHub.sender)
  }
}

// Will be used by the deploymentScript to do all setup work
// If there's no existing service principal for AZURE_METERING_INFRA_CLIENT_ID, create a UAMI
resource aggregatorInfrastructureIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2018-11-30' = if (empty(AZURE_METERING_INFRA_CLIENT_ID)) {
  name: names.identity
  location: location
  tags: {
    prefix: appNamePrefix
  }
}

// If there's no existing service principal for AZURE_METERING_INFRA_CLIENT_ID, authorize the UAMI on Event Hub
resource eventhubRoleAssignmentBackendUAMI 'Microsoft.Authorization/roleAssignments@2020-08-01-preview' = if (empty(AZURE_METERING_INFRA_CLIENT_ID)) {
  name: guid(aggregatorInfrastructureIdentity.name, eventHub.name, names.roleDefinitions.eventHub.dataOwner)
  scope: eventHub
  properties: {
    description: '${aggregatorInfrastructureIdentity.name} should be a Azure Event Hubs Data Owner on ${eventHub.id}'
    principalId: aggregatorInfrastructureIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', names.roleDefinitions.eventHub.dataOwner)
  }
}

// If there's an existing service principal for AZURE_METERING_INFRA_CLIENT_ID, authorize that SP on Event Hub
resource eventhubRoleAssignmentBackendExternalSP 'Microsoft.Authorization/roleAssignments@2020-08-01-preview' = if (!empty(AZURE_METERING_INFRA_CLIENT_ID)) {
  name: guid(AZURE_METERING_INFRA_CLIENT_ID, eventHub.name, names.roleDefinitions.eventHub.dataOwner)
  scope: eventHub
  properties: {
    description: '${AZURE_METERING_INFRA_CLIENT_ID} should be a Azure Event Hubs Data Owner on ${eventHub.id}'
    principalId: AZURE_METERING_INFRA_CLIENT_ID
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', names.roleDefinitions.eventHub.dataOwner)
  }
}

resource storageRoleAssignmentInfraUAMI 'Microsoft.Authorization/roleAssignments@2020-08-01-preview' = if (empty(AZURE_METERING_INFRA_CLIENT_ID)) {
  name: guid(aggregatorInfrastructureIdentity.name, captureAndStateStorageAccount.name, names.roleDefinitions.blob.contributor)
  scope: captureAndStateStorageAccount
  properties: {
    description: '${aggregatorInfrastructureIdentity.name} should be a Blob Contributor on ${captureAndStateStorageAccount.id}'
    principalId: aggregatorInfrastructureIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', names.roleDefinitions.blob.contributor)
  }
}

resource storageRoleAssignmentInfraServicePrincipal 'Microsoft.Authorization/roleAssignments@2020-08-01-preview' = if (!empty(AZURE_METERING_INFRA_CLIENT_ID)) {
  name: guid(AZURE_METERING_INFRA_CLIENT_ID, captureAndStateStorageAccount.name, names.roleDefinitions.blob.contributor)
  scope: captureAndStateStorageAccount
  properties: {
    description: '${AZURE_METERING_INFRA_CLIENT_ID} should be a Blob Contributor on ${captureAndStateStorageAccount.id}'
    principalId: AZURE_METERING_INFRA_CLIENT_ID
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', names.roleDefinitions.blob.contributor)
  }
}

output storageId string = captureAndStateStorageAccount.id
output storageName string = captureAndStateStorageAccount.name
output captureBlobEndpoint string = 'https://${captureAndStateStorageAccount.name}.blob.${environment().suffixes.storage}/${names.containers.capture}'
output snapshotsBlobEndpoint string = 'https://${captureAndStateStorageAccount.name}.blob.${environment().suffixes.storage}/${names.containers.snapshots}'
output checkpointBlobEndpoint string = 'https://${captureAndStateStorageAccount.name}.blob.${environment().suffixes.storage}/${names.containers.checkpoint}'
output aggregatorInfrastructureIdentityId string = (empty(AZURE_METERING_INFRA_CLIENT_ID) ? aggregatorInfrastructureIdentity.id : AZURE_METERING_INFRA_CLIENT_ID)
output eventHubNamespaceName string = names.eventHub.namespaceName
output eventHubName string = names.eventHub.hubName
