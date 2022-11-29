// name should not contain underscores

@description('Prefix for created resources.')
param appNamePrefix string

@description('Location for all resources.')
param location string = resourceGroup().location

@description('Must be true if you want to use the analytics queries')
param isHnsEnabled bool = true

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

@description('The object ID of the sending application.')
param ADObjectID string

@secure()
param ADApplicationSecret string 

@description('Do not change this if you want to use the analytics queries')
param AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT string ='{Namespace}/{EventHub}/p={PartitionId}/y={Year}/m={Month}/d={Day}/h={Hour}/mm={Minute}/{Second}'

param deployAppInsights bool = false

// EventHub and Storage Account
module data 'modules/data.bicep' = {
  name: '${appNamePrefix}-data'
  params: {
    appNamePrefix: appNamePrefix
    location: location
    isHnsEnabled: isHnsEnabled
    skuCapacity: skuCapacity
    eventHubSku: eventHubSku
    archiveNameFormat: AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT
    senderObjectId: ADObjectID
  }
}

module marketplaceMeteringAggregatorApp './modules/container-app.bicep' = {
  name: '${appNamePrefix}-container-app'
  params: {
    enableIngress: false
    isExternalIngress: false
    location: location
    infrastructureManagedIdentityId: data.outputs.aggregatorInfrastructureIdentityId
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
      { name: 'AZURE_METERING_MARKETPLACE_TENANT_ID',         value: subscription().tenantId }
      { name: 'AZURE_METERING_MARKETPLACE_CLIENT_ID',         value: ADApplicationID }
      { name: 'AZURE_METERING_MARKETPLACE_CLIENT_SECRET',     value: ADApplicationSecret }
      { name: 'AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME',  value: data.outputs.eventHubNamespaceName }
      { name: 'AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME',   value: data.outputs.eventHubName }
      { name: 'AZURE_METERING_INFRA_SNAPSHOTS_CONTAINER',     value: data.outputs.snapshotsBlobEndpoint }
      { name: 'AZURE_METERING_INFRA_CHECKPOINTS_CONTAINER',   value: data.outputs.checkpointBlobEndpoint }
      { name: 'AZURE_METERING_INFRA_CAPTURE_CONTAINER',       value: data.outputs.captureBlobEndpoint }
      { name: 'AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT', value: AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT }
    ]
  }
}

// Container Apps 
module environment './modules/environment.bicep' = if (deployAppInsights) {
  name: '${appNamePrefix}-environment'
  params: {
    environmentName: '${appNamePrefix}-env'
    location: location
    appInsightsName: '${appNamePrefix}-ai'
    logAnalyticsWorkspaceName: '${appNamePrefix}-la'
  }
}

output eventHubName string = data.outputs.eventHubName
