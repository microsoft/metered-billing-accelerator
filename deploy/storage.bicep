targetScope = 'resourceGroup'

param appNamePrefix string
param location string = resourceGroup().location

resource mainstorage 'Microsoft.Storage/storageAccounts@2021-08-01' = {
  kind: 'StorageV2'
  location: location
  name: '${appNamePrefix}sa'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    allowSharedKeyAccess: true
    defaultToOAuthAuthentication: true
    encryption: {
      keySource: 'Microsoft.Storage'
      services: {
        blob: {
          enabled: true
          keyType: 'Account'
        }
        file: {
          enabled: true
          keyType: 'Account'
        }
      }
    }
    minimumTlsVersion: 'TLS1_2'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
      ipRules: []
      virtualNetworkRules: []
    }
    supportsHttpsTrafficOnly: true
  }
  sku: {
    name: 'Standard_RAGRS'
  }
}


resource capturecontainer  'Microsoft.Storage/storageAccounts/blobServices/containers@2021-08-01' = {
  name: '${mainstorage.name}/default/capture'
}

resource snapshots  'Microsoft.Storage/storageAccounts/blobServices/containers@2021-08-01' = {
  name: '${mainstorage.name}/default/snapshots'
}

resource checkpoint  'Microsoft.Storage/storageAccounts/blobServices/containers@2021-08-01' = {
  name: '${mainstorage.name}/default/checkpoint'
}




output storageId string = mainstorage.id
