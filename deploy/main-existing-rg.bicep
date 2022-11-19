// name should not contain underscores

@description('Prefix for created resources.')
param appNamePrefix string

param location string = resourceGroup().location

//Storage Account params
param isHnsEnabled bool = true //Must be true if you want to use the analytics queries

//Eventhub params
@allowed([
  'Standard'
  'Basic'
])
param eventHubSku string = 'Standard'

@allowed([
  1
  2
  4
])
param skuCapacity int = 1
param consumerGroupName string = 'consumerGroupName'

//Container App params 
param minReplicas int = 1

param containerImage string = 'ghcr.io/gjoshevski/metered-billing-accelerator:main'
param isPrivateRegistry bool = false

param containerRegistry string  = 'ghcr.io'
param containerRegistryUsername string = ''
@secure()
param registryPassword string = ''

//App params
param ADApplicationID string
param ADApplicationSecret string 
param tenantID string
param AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT string ='{Namespace}/{EventHub}/p={PartitionId}/y={Year}/m={Month}/d={Day}/h={Hour}/mm={Minute}/{Second}' //Do not change this if you want to use the analytics queries

// Storage Account
module storage 'modules/storage.bicep' = {
  name: '${appNamePrefix}-storage'
  params: {
    appNamePrefix: appNamePrefix
    location: location
    isHnsEnabled: isHnsEnabled
  }
}

//Event Hubs
module eventhub 'modules/eventhub.bicep' = {
  name: '${appNamePrefix}-eventhub'
  params: {
    appNamePrefix: appNamePrefix
    skuCapacity: skuCapacity
    eventHubSku: eventHubSku
    storageAccountId: storage.outputs.storageId
    consumerGroupName: consumerGroupName
    location: location
    captureContainerName: storage.outputs.captureContainerName
    archiveNameFormat: AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT

  }
}

// Container Apps 
module environment './modules/environment.bicep' = {
  name: '${appNamePrefix}-environment'
  params: {
    environmentName: '${appNamePrefix}-env'
    location: location
    appInsightsName: '${appNamePrefix}-ai'
    logAnalyticsWorkspaceName: '${appNamePrefix}-la'
  }
}

module marketplaceMeteringAggregatorApp './modules/container-app.bicep' = {
  name: '${appNamePrefix}-container-app'
  dependsOn: [
    environment
  ]
  params: {
    enableIngress: false
    isExternalIngress: false
    location: location
    environmentName: '${appNamePrefix}-env'
    containerAppName: '${appNamePrefix}-app'
    containerImage: containerImage
    isPrivateRegistry: isPrivateRegistry 
    minReplicas: minReplicas
    containerRegistry: containerRegistry
    registryPassword: registryPassword
    containerRegistryUsername: containerRegistryUsername
    containerPort: 80
    env: [
      {
        name: 'AZURE_METERING_MARKETPLACE_CLIENT_ID'
        value: ADApplicationID
      }
      {
        name: 'AZURE_METERING_MARKETPLACE_CLIENT_SECRET'
        value: ADApplicationSecret
      }
      {
        name: 'AZURE_METERING_MARKETPLACE_TENANT_ID'
        value: tenantID
      }
      {
        name: 'AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME'
        value: eventhub.outputs.eventHubNamespaceName
      }
      {
        name: 'AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME'
        value: eventhub.outputs.eventHubInstanceName
      }
      {
        name: 'AZURE_METERING_INFRA_CHECKPOINTS_CONTAINER'
        value: storage.outputs.checkpointBlobEndpoint
      }
      {
        name: 'AZURE_METERING_INFRA_SNAPSHOTS_CONTAINER'
        value: storage.outputs.snapshotsBlobEndpoint
      }
      {
        name: 'AZURE_METERING_INFRA_CAPTURE_CONTAINER'
        value: storage.outputs.captureBlobEndpoint
      }
      {
        name: 'AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT'
        value: AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT
      }
    ]
  }
}

module rbac './modules/role-assignments.bicep' = {
  name: '${appNamePrefix}-rbac'
  params: {
    eventHubName: eventhub.outputs.eventHubName
    storageName: storage.outputs.storageName
    principalId: marketplaceMeteringAggregatorApp.outputs.principalId
    appNamePrefix: appNamePrefix
  }
}

output eventHubConnectionString string = eventhub.outputs.eventHubNamespaceConnectionString
output eventHubName string = eventhub.outputs.eventHubInstanceName

