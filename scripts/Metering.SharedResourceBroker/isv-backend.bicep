@description('Specifies a suffix to append to resource names.')
param suffix string

@description('Specifies the Azure location where the resources should be created.')
param location string = resourceGroup().location

@description('The secret query parameter of the Notification Endpoint URL, which is configured in the Partner Center Dashboard / Marketplace offers / Plan overview / Technical Configuration')
@secure()
param notificationSecretValue string 

@description('The value for the bootstrap secret, i.e. the secret which will be used as an API token by the deploymentScript.')
@secure()
param bootstrapSecretValue string 

@description('The app ID from the Appliance Resource Provider, i.e. az ad sp list --display-name Appliance Resource Provider | jq .[0].id')
param applianceResourceProviderObjectID string

@description('The ID of the security group where new service principals should be added to')
param securityGroupForServicePrincipal string 

param deploymentZip string = 'https://isvreleases.blob.core.windows.net/backendrelease/henrikwh/SharedResourceBroker/Backend/Publish/Backend-Debug-ea838823b7bbe587bfa659f45c9e0e0fac51327a.zip'

var names = {
  secrets: {
    bootstrapSecret: 'BootstrapSecret'
    notificationSecret: 'NotificationSecret'
  }
  publisherKeyVault: 'SRBKV${suffix}'
  uami: 'SRBIdentity${suffix}'
  appInsights: 'SRBAppInsights${suffix}'
  appServicePlan: 'SRBServicePlan${suffix}'
  appService: 'SRBWebapp${suffix}'
}

var keyVaultRoleID = {
  'Contributor': 'b24988ac-6180-42a0-ab88-20f7382dd24c'
  'Owner': '8e3af657-a8ff-443c-a75c-2fe8c4bcb635'
  'Reader': 'acdd72a7-3385-48ef-bd42-f606fba81ae7'
  'Key Vault Administrator': '00482a5a-887f-4fb3-b363-3b7fe8e74483'
  'Key Vault Certificates Officer': 'a4417e6f-fecd-4de8-b567-7b0420556985'
  'Key Vault Crypto Officer': '14b46e9e-c2b7-41b4-b07b-48a6ebf60603'
  'Key Vault Crypto Service Encryption User': 'e147488a-f6f5-4113-8e2d-b22465e65bf6'
  'Key Vault Crypto User': '12338af0-0e69-4776-bea7-57ae8d297424'
  'Key Vault Reader': '21090545-7ca7-4776-b22c-e363652d74d2'
  'Key Vault Secrets Officer': 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
  'Key Vault Secrets User': '4633458b-17de-408a-b874-0445c86b69e6'
}

resource publisherKeyVault 'Microsoft.KeyVault/vaults@2021-11-01-preview' = {
  name: names.publisherKeyVault
  location: location
  properties: {
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: true
    enableRbacAuthorization: true
    tenantId: subscription().tenantId
    sku: { name: 'standard', family: 'A' }
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }

 
}

resource bootstrapSecret 'Microsoft.KeyVault/vaults/secrets@2021-11-01-preview' = {
  parent: publisherKeyVault
  name: names.secrets.bootstrapSecret
  properties: {
    value: bootstrapSecretValue
  }
}

resource notificationSecret 'Microsoft.KeyVault/vaults/secrets@2021-11-01-preview' = {
  parent: publisherKeyVault
  name: names.secrets.notificationSecret
  properties: {
    value: notificationSecretValue
  }
}

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2021-09-30-preview' = {
  name: names.uami
  location: location
}

resource managedIdentityCanEnumerateSecrets 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid(keyVaultRoleID['Key Vault Reader'], uami.id, publisherKeyVault.id)
  scope: publisherKeyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultRoleID['Key Vault Reader'])
    principalId: uami.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource managedIdentityCanReadBootstrapSecret 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid(keyVaultRoleID['Key Vault Secrets User'], uami.id, bootstrapSecret.id)
  scope: bootstrapSecret
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultRoleID['Key Vault Secrets User'])
    principalId: uami.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource managedIdentityCanReadNotificationSecret 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid(keyVaultRoleID['Key Vault Secrets User'], uami.id, notificationSecret.id)
  scope: notificationSecret
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultRoleID['Key Vault Secrets User'])
    principalId: uami.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource applianceResourceProviderCanReadBootstrapSecret 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid(keyVaultRoleID['Contributor'], 'Appliance Resource Provider', applianceResourceProviderObjectID, publisherKeyVault.id)
  scope: publisherKeyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultRoleID['Contributor'])
    principalId: applianceResourceProviderObjectID
    principalType: 'ServicePrincipal'
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: names.appInsights
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2020-06-01' = {
  name: names.appServicePlan
  location: location
  sku: { name: 'D1', capacity: 1 }
}

resource appService 'Microsoft.Web/sites@2021-03-01' = {
  name: names.appService
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${uami.id}': {} }
  }
  dependsOn: [ appInsights ]
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      minTlsVersion: '1.2'
      use32BitWorkerProcess: true
      netFrameworkVersion: 'v6.0'
    }
  }

  resource settings 'config@2021-03-01' = {
    name: 'appsettings'
    dependsOn: [ uami ]
    properties: {
      // On Windows, separate with ':', on Linux with '__'
      'ServicePrincipalCreatorSettings:GeneratedServicePrincipalPrefix': 'ManagedProductServicePrincipal'
      'ServicePrincipalCreatorSettings:SharedResourcesGroup': securityGroupForServicePrincipal
      'ServicePrincipalCreatorSettings:KeyVaultName': publisherKeyVault.name
      'ServicePrincipalCreatorSettings:AzureADManagedIdentityClientId': uami.properties.clientId
      //'SCM_DO_BUILD_DURING_DEPLOYMENT': 'true'
      'ApplicationInsights:ConnectionString' : appInsights.properties.ConnectionString
      'WEBSITE_RUN_FROM_PACKAGE': deploymentZip
    }
  }

  resource connectionstrings 'config@2021-03-01' = {
    name: 'connectionstrings'
    properties: {
      'APPLICATIONINSIGHTS_CONNECTION_STRING': {
        type: 'Custom'
        value: appInsights.properties.ConnectionString
      }
    }
  }
  
  resource logs 'config@2021-03-01' = {
    name: 'logs'
    properties: {
      applicationLogs: {
        fileSystem: {
          level: 'Warning'
        }
      }
      httpLogs: {
        fileSystem: {
          retentionInMb: 40
          enabled: true
        }
      }
      failedRequestsTracing: { enabled: true }
      detailedErrorMessages: { enabled: true }
    }
  }

  // resource zipDeploy 'extensions@2021-02-01' = {
  //   name: 'MSDeploy'
  //   properties: {
  //     packageUri: 'https://isvreleases.blob.core.windows.net/backendrelease/henrikwh/SharedResourceBroker/Backend/src.zip'
  //   }
  // }  
}

// resource deploymentScript 'Microsoft.Resources/deploymentScripts@2019-10-01-preview' = {
//   name: 'dc'
//   location: location
//   identity: {
//     type: 'UserAssigned'
//     userAssignedIdentities: {
//       '${uami.id}': {}
//     }
//   }
//   kind: 'AzureCLI'
//   properties: {
//     azCliVersion: '2.37.0'
//     timeout: 'PT30M'
//     retentionInterval: 'P1D'
//     cleanupPreference: 'OnExpiration'
//     environmentVariables: [
//       {
//         name: 'scmUri'
//         value: list(resourceId('Microsoft.Web/sites/config', appService.name, 'publishingcredentials'), appService.apiVersion).properties.scmUri
//       }
//       {
//         name: 'zipUrl'
//         value: 'https://isvreleases.blob.core.windows.net/backendrelease/henrikwh/SharedResourceBroker/Backend/src.zip'
//       }
//     ]
//     scriptContent: '''
//     curl --silent --request POST --url "${scmUri}/api/publish?type=zip" --header "Content-Type: application/json" --data "$( echo "{}" | jq --arg x "${zipUrl}" '.packageUri=$x' )" | jq
//     '''
//   }
// }

// https://docs.microsoft.com/en-us/azure/app-service/deploy-run-package#run-from-external-url-instead

output managedIdentityPrincipalID string = uami.properties.principalId
output appServiceName string = names.appService
output resourceNames object = names

// output scmUri string = '${list(resourceId('Microsoft.Web/sites/config', appService.name, 'publishingcredentials'), appService.apiVersion).properties.scmUri}'
// output publishingUserName string = '${list(resourceId('Microsoft.Web/sites/config', appService.name, 'publishingcredentials'), appService.apiVersion).properties.publishingUserName}'
// output publishingPassword string = '${list(resourceId('Microsoft.Web/sites/config', appService.name, 'publishingcredentials'), appService.apiVersion).properties.publishingPassword}'

