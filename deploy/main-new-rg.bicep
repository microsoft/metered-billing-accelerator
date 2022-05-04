targetScope = 'subscription'
param appNamePrefix string

param rgLocation string

param ADApplicationID string
param ADApplicationSecret string 
param tenantID string

resource sa 'Microsoft.Resources/resourceGroups@2021-01-01' = {
  name: '${appNamePrefix}-rg'
  location: rgLocation
}

module rgDeployment 'main-existing-rg.bicep' = {
  name: '${appNamePrefix}-rg-deployment'
  params: {
    appNamePrefix: appNamePrefix
    location: rgLocation
    ADApplicationID: ADApplicationID
    ADApplicationSecret: ADApplicationSecret
    tenantID: tenantID
  }
  scope: resourceGroup(sa.name)
}

output eventHubConnectionString string = rgDeployment.outputs.eventHubConnectionString
output eventHubName string = rgDeployment.outputs.eventHubName
