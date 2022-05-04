targetScope = 'subscription'
param appNamePrefix string
@allowed([
  'northcentralusstage'
  'westcentralus'
  'westeurope'
  'jioindiawest'
  'northeurope'
  'canadacentral'
  'eastus2'
  'eastus'
])
param location string = 'westeurope'

param ADApplicationID string
param ADApplicationSecret string 
param tenantID string

resource sa 'Microsoft.Resources/resourceGroups@2021-01-01' = {
  name: '${appNamePrefix}-rg'
  location: location
}

module rgDeployment 'main-existing-rg.bicep' = {
  name: '${appNamePrefix}-rg-deployment'
  params: {
    appNamePrefix: appNamePrefix
    location: location
    ADApplicationID: ADApplicationID
    ADApplicationSecret: ADApplicationSecret
    tenantID: tenantID
  }
  scope: resourceGroup(sa.name)
}

output eventHubConnectionString string = rgDeployment.outputs.eventHubConnectionString
output eventHubName string = rgDeployment.outputs.eventHubName
