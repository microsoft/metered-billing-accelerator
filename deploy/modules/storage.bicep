targetScope = 'resourceGroup'

param appNamePrefix string
param location string = resourceGroup().location
param isHnsEnabled bool
param captureContainerName string = 'capture'
param snapshotContainerName string = 'snapshot'
param checkpointContainerName string = 'checkpoint'

resource mainstorage 'Microsoft.Storage/storageAccounts@2021-08-01' = {
  kind: 'StorageV2'
  location: location
  name: '${appNamePrefix}sa'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowCrossTenantReplication: true
    allowSharedKeyAccess: true
    defaultToOAuthAuthentication: true
    isHnsEnabled: isHnsEnabled
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


resource capture  'Microsoft.Storage/storageAccounts/blobServices/containers@2021-08-01' = {
  name: '${mainstorage.name}/default/${captureContainerName}'
}

resource snapshots  'Microsoft.Storage/storageAccounts/blobServices/containers@2021-08-01' = {
  name: '${mainstorage.name}/default/${snapshotContainerName}'
}

resource checkpoint  'Microsoft.Storage/storageAccounts/blobServices/containers@2021-08-01' = {
  name: '${mainstorage.name}/default/${checkpointContainerName}'
}


output storageId string = mainstorage.id
output captureBlobEndpoint string  = 'https://${mainstorage.name}.blob.${environment().suffixes.storage}/${captureContainerName}'
output snapshotsBlobEndpoint string  = 'https://${mainstorage.name}.blob.${environment().suffixes.storage}/${snapshotContainerName}'
output checkpointBlobEndpoint string  = 'https://${mainstorage.name}.blob.${environment().suffixes.storage}/${checkpointContainerName}'
output storageName string = mainstorage.name
output captureContainerName string = captureContainerName
