targetScope = 'resourceGroup'

param appNamePrefix string
param location string = resourceGroup().location

param isHnsEnabled bool = false
param captureContainerName string = 'capture'
param snapshotContainerName string = 'snapshot'
param checkpointContainerName string = 'checkpoint'

resource mainstorage 'Microsoft.Storage/storageAccounts@2022-05-01' = {
  name: '${appNamePrefix}sa'
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_RAGRS' }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2', supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    defaultToOAuthAuthentication: true
    isHnsEnabled: isHnsEnabled
  }
}

resource capture 'Microsoft.Storage/storageAccounts/blobServices/containers@2022-05-01' = {
  name: '${mainstorage.name}/default/${captureContainerName}'
}

resource snapshots 'Microsoft.Storage/storageAccounts/blobServices/containers@2022-05-01' = {
  name: '${mainstorage.name}/default/${snapshotContainerName}'
}

resource checkpoint 'Microsoft.Storage/storageAccounts/blobServices/containers@2022-05-01' = {
  name: '${mainstorage.name}/default/${checkpointContainerName}'
}

output storageId string = mainstorage.id
output captureBlobEndpoint string  = 'https://${mainstorage.name}.blob.${environment().suffixes.storage}/${captureContainerName}'
output snapshotsBlobEndpoint string  = 'https://${mainstorage.name}.blob.${environment().suffixes.storage}/${snapshotContainerName}'
output checkpointBlobEndpoint string  = 'https://${mainstorage.name}.blob.${environment().suffixes.storage}/${checkpointContainerName}'
output storageName string = mainstorage.name
output captureContainerName string = captureContainerName
