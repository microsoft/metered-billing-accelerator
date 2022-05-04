targetScope = 'subscription'
param appNamePrefix string
@allowed([
  'northcentralusstage'
  'westcentralus,eastus'
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

module storage 'main-existing-rg.bicep' = {
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
