targetScope = 'resourceGroup'

param appNamePrefix string
param eventHubSku string = 'Standard'
param skuCapacity int = 1
param location string = resourceGroup().location
param storageAccountId string
param consumerGroupName string = 'consumerGroupName'
param captureContainerName string = 'capture'
param archiveNameFormat string
param messageRetentionInDays int = 3
param partitionCount int = 5

resource eh_namespace 'Microsoft.EventHub/namespaces@2021-11-01' = {
  name: '${appNamePrefix}-eh-namespace'
  location: location
  sku: {
    name: eventHubSku
    tier: eventHubSku
    capacity: skuCapacity
  }
  properties: {
    disableLocalAuth: false
    isAutoInflateEnabled: false
    kafkaEnabled: true
    maximumThroughputUnits: 0
  }
}

resource eventHub 'Microsoft.EventHub/namespaces/eventhubs@2021-11-01' = {
  name: '${eh_namespace.name}/${appNamePrefix}-eh'
  properties: {
    captureDescription: {
      enabled: true
      encoding: 'Avro'
      destination: {
        name: 'EventHubArchive.AzureBlockBlob'
        properties: {
          storageAccountResourceId: storageAccountId
          archiveNameFormat: archiveNameFormat
          blobContainer: captureContainerName
        }
      }
      intervalInSeconds: 300
      sizeLimitInBytes: 314572800
      skipEmptyArchives: true
    }
    messageRetentionInDays: messageRetentionInDays
    partitionCount: partitionCount
    status: 'Active'
  }
}

resource eventHub_Send 'Microsoft.EventHub/namespaces/eventhubs/authorizationRules@2021-01-01-preview' = {
  parent: eventHub
  name: 'MarketplaceAggregator'
  properties: {
    rights: [
      'Send'
      'Listen'
      'Manage'
    ]
  }
  dependsOn: [
    eh_namespace
  ]
}


resource consumerGroup 'Microsoft.EventHub/namespaces/eventhubs/consumergroups@2017-04-01' = {
  name: '${eventHub.name}/${consumerGroupName}'
  properties: {}
  dependsOn: [
    eh_namespace
  ]
}

// Determine our connection string
var eventHubNamespaceConnectionString = listKeys(eventHub_Send.id, eventHub_Send.apiVersion).primaryConnectionString

// Output our variables

output eventHubNamespaceConnectionString string = eventHubNamespaceConnectionString
output eventHubNamespaceName string = eh_namespace.name
output eventHubName string = eventHub.name
output eventHubInstanceName string = '${appNamePrefix}-eh'


