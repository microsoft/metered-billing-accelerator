//az deployment sub create -f main.bicep --parameters appNamePrefix='testdepmetter' --parameters consumerGroupName=consumerGroupName -l ukwest   
// name should not contain underscore 

param appNamePrefix string
@allowed([
  'northcentralusstage'
  'westcentralus,eastus'
  'westeurope'
  'jioindiawest'
  'northeurope'
  'canadacentral'
  'eastus2'
])
param region string = 'westeurope'

targetScope = 'subscription'

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
param consumerGroupName string


resource sa 'Microsoft.Resources/resourceGroups@2021-01-01' = {
  name: '${appNamePrefix}-rg'
  location: region
}

module storage './storage.bicep' = {
  name: 'storageDeploy'
  params: {
    appNamePrefix: appNamePrefix
    location: region
  }
  scope: resourceGroup(sa.name)
}

module eventhub './eventhub.bicep' = {
  name: 'eventhubDeploy'
  params: {
    appNamePrefix: appNamePrefix
    skuCapacity: skuCapacity
    eventHubSku: eventHubSku
    storageAccountId: storage.outputs.storageId
    consumerGroupName: consumerGroupName
    location: region

  }
  scope: resourceGroup(sa.name)
}

/*
module acr './containerregistry.bicep' = {
  name: 'acr'
  params: {
    acrName: appNamePrefix
    location: region
  }
  scope: resourceGroup(sa.name)
}
*/

module containerapp './containerapp.bicep' = {
  name: '${appNamePrefix}-agregator'
  params: {
    nodeImage: 'mcr.microsoft.com/azure-functions/node:10-alpine'
    containerRegistry: 'ghcr.io'
    AZURE_METERING_MARKETPLACE_CLIENT_ID: 'todo'
    location: region
    }
  scope: resourceGroup(sa.name)
}


