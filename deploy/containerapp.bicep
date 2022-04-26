param nodeImage string

param containerRegistry string
param containerRegistryUsername string
@secure()
param containerRegistryPassword string


@secure()
param AZURE_METERING_MARKETPLACE_CLIENT_ID string

param location string = resourceGroup().location

var environmentName = 'env-${uniqueString(resourceGroup().id)}'
var minReplicas = 0

var nodeServiceAppName = 'node-app'
var workspaceName = '${nodeServiceAppName}-log-analytics'
var appInsightsName = '${nodeServiceAppName}-app-insights'

var containerRegistryPasswordRef = 'container-registry-password'

resource workspace 'Microsoft.OperationalInsights/workspaces@2020-08-01' = {
  name: workspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {}
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02-preview' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Flow_Type: 'Bluefield'
  }
}

resource environment 'Microsoft.Web/kubeEnvironments@2021-03-01' = {
  name: environmentName
  location: location
  properties: {
    environmentType: 'managed'
    internalLoadBalancerEnabled: false
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: listKeys(workspace.id, workspace.apiVersion).primarySharedKey
      }
    }
    containerAppsConfiguration: {
      daprAIInstrumentationKey: appInsights.properties.InstrumentationKey
    }
  }
}

resource containerApp 'Microsoft.Web/containerapps@2021-03-01' = {
  name: nodeServiceAppName
  kind: 'containerapps'
  location: location
  properties: {
    kubeEnvironmentId: environment.id
    configuration: {
      secrets: [
        {
          name: containerRegistryPasswordRef
          value: containerRegistryPassword
        }
      ]
      registries: [
        {
          server: containerRegistry
          username: containerRegistryUsername
          passwordSecretRef: containerRegistryPasswordRef
        }
      ]
    }
    template: {
      containers: [
        {
          image: nodeImage
          name: nodeServiceAppName
          env: [
            {
              name: 'AZURE_METERING_MARKETPLACE_CLIENT_ID'
              value: AZURE_METERING_MARKETPLACE_CLIENT_ID
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
      }
    }
  }
}
