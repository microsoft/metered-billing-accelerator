targetScope = 'resourceGroup'

param appNamePrefix string
param eventHubSku string = 'Standard'
param skuCapacity int = 1
param location string = resourceGroup().location
param storageAccountId string
param consumerGroupName string

resource eh_namespace 'Microsoft.EventHub/namespaces@2021-11-01' = {
  name: '${appNamePrefix}-eh-namespace'
  location: location
  sku: {
    name: eventHubSku
    tier: eventHubSku
    capacity: skuCapacity
  }
  properties: {  //TODO: Check this values 
    disableLocalAuth: false
    isAutoInflateEnabled: false
    kafkaEnabled: true
    maximumThroughputUnits: 0
  }
}


resource eventHub 'Microsoft.EventHub/namespaces/eventhubs@2017-04-01' = {
  name: '${eh_namespace.name}/${appNamePrefix}-eh'
  properties: {
    captureDescription: {
      destination: {
        name: 'EventHubArchive.AzureBlockBlob'
        properties: {
          archiveNameFormat: '{Namespace}/{EventHub}/p{PartitionId}--{Year}-{Month}-{Day}--{Hour}-{Minute}-{Second}'
          blobContainer: '${appNamePrefix}-capture'
          storageAccountResourceId: storageAccountId
        }
      }
      enabled: true
      encoding: 'Avro'
      intervalInSeconds: 300
      sizeLimitInBytes: 314572800
      skipEmptyArchives: true
    }
    messageRetentionInDays: 2
    partitionCount: 11
    status: 'Active'
  }
}

resource eventHub_Send 'Microsoft.EventHub/namespaces/eventhubs/authorizationRules@2021-01-01-preview' = {
  parent: eventHub
  name: 'Send'
  properties: {
    rights: [
      'Send'
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
output eventHubName string = '${appNamePrefix}-eh-namespace'

