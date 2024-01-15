@description('Specifies a seed to generate the resource name prefix from.')
param seed string

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

param deploymentZip string = 'https://github.com/microsoft/metered-billing-accelerator/releases/download/1.0.3-beta/Aggregator.windows-latest.1.0.3-beta.zip'

param useAppInsights bool = true

var prefix = toLower('sp${uniqueString(seed)}')

var windowsInstance = true // When going for Linux, you must also update the config setting names to use __ instead of : (see below)

// The ARM [`substring`](https://learn.microsoft.com/en-us/azure/azure-resource-manager/templates/template-functions-string#substring) function is difficult to use. `substring('Hallo', 0, 10)` currently throws the error `The index and length parameters must refer to a location within the string.`. In languages such as C# etc., the length parameter is treated as a maximum, not an absolute. If the provided input string is shorter than the length, the function should just return the original input string. Workaround is writing something like `substring('Hallo', 0, min(10, length('Hallo')))`, which is cumbersome.
var names = {
  uami: '${prefix}-service-principal-generator'
  appInsights: prefix
  appServicePlan: prefix
  appService: substring(prefix, 0, min(40, length(prefix)))
  publisherKeyVault: substring(prefix, 0, min(24, length(prefix)))
  secrets: {
    bootstrapSecret: 'BootstrapSecret'
    notificationSecret: 'NotificationSecret'
  }
  prefix: prefix
}

var keyVaultRoleID = {
  Contributor: 'b24988ac-6180-42a0-ab88-20f7382dd24c'
  Owner: '8e3af657-a8ff-443c-a75c-2fe8c4bcb635'
  Reader: 'acdd72a7-3385-48ef-bd42-f606fba81ae7'
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
  tags: {
    prefix: prefix
  }
  properties: {
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: true // https://learn.microsoft.com/en-us/azure/azure-resource-manager/managed-applications/key-vault-access
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
  tags: {
    prefix: prefix
  }
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

resource managedIdentityIsSecretsOfficer 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  // The UAMI must be a secrets officer, as it not only reads the Bootstrap and Notification secret, but also stores service principal secrets in there.
  name: guid(keyVaultRoleID['Key Vault Secrets Officer'], uami.id, publisherKeyVault.id)
  scope: publisherKeyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultRoleID['Key Vault Secrets Officer'])
    principalId: uami.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// resource managedIdentityCanReadNotificationSecret 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
//   name: guid(keyVaultRoleID['Key Vault Secrets User'], uami.id, notificationSecret.id)
//   scope: notificationSecret
//   properties: {
//     roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultRoleID['Key Vault Secrets User'])
//     principalId: uami.properties.principalId
//     principalType: 'ServicePrincipal'
//   }
// }

resource applianceResourceProviderCanReadSecrets 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  // The 'Appliance Resource Provider' is the identity under which the ARM deployment in the managed resource group runs.
  // Therefore, it must both be able to read the BootstrapSecret, but also the service principal secret for the current managed app deployment.
  name: guid(keyVaultRoleID.Contributor, 'Appliance Resource Provider', applianceResourceProviderObjectID, publisherKeyVault.id)
  scope: publisherKeyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultRoleID.Contributor)
    principalId: applianceResourceProviderObjectID
    principalType: 'ServicePrincipal'
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = if (useAppInsights) {
  name: names.appInsights
  location: location
  tags: {
    prefix: prefix
  }
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2022-03-01' = {
  name: names.appServicePlan
  location: location
  tags: {
    prefix: prefix
  }
  sku: { name: 'S1', tier: 'Standard', size: 'S1', capacity: 1 }
  properties: {
    reserved: windowsInstance ? false : true
  }
}

resource appService 'Microsoft.Web/sites@2021-03-01' = {
  name: names.appService
  location: location
  tags: {
    prefix: prefix
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { 
      '${uami.id}': {}
    }
  }
  kind: windowsInstance ? 'app' : 'linux'
  properties: {
    serverFarmId: appServicePlan.id
    reserved: windowsInstance ? false : true
    clientAffinityEnabled: false
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      netFrameworkVersion: windowsInstance ? 'v8.0' : null
      linuxFxVersion: windowsInstance ? null : 'DOTNETCORE|8.0' // This is only for Linux *containers*
      // windowsFxVersion: 'dotnet:8'     // This is for Windows *containers* (code-name Xenon)
      use32BitWorkerProcess: false
    }
  }

  resource metadata 'config@2021-03-01' = if (windowsInstance) {
    name: 'metadata'
    properties: {
      CURRENT_STACK: 'dotnet' // This should be 'dotnetcore' only for .NET Core 3.x..
    }
  }

  resource settings 'config@2021-03-01' = {
    name: 'appsettings'
    dependsOn: [ metadata ]
    properties: {
      // On Windows, separate with ':', on Linux with '__'
      'ApplicationInsights:ConnectionString': useAppInsights ? appInsights.properties.ConnectionString : ''
      'ServicePrincipalCreatorSettings:GeneratedServicePrincipalPrefix': 'ManagedProductServicePrincipal'
      'ServicePrincipalCreatorSettings:SharedResourcesGroup': securityGroupForServicePrincipal
      'ServicePrincipalCreatorSettings:KeyVaultName': publisherKeyVault.name
      'ServicePrincipalCreatorSettings:AzureADManagedIdentityClientId': uami.properties.clientId
      //'SCM_DO_BUILD_DURING_DEPLOYMENT': 'true'
      WEBSITE_RUN_FROM_PACKAGE: deploymentZip
    }
  }

  // resource connectionstrings 'config@2021-03-01' = {
  //   name: 'connectionstrings'
  //   properties: {
  //     APPLICATIONINSIGHTS_CONNECTION_STRING: {
  //       type: 'Custom'
  //       value: appInsights.properties.ConnectionString
  //     }
  //   }
  // }
  
  resource logs 'config@2021-03-01' = if (useAppInsights) {
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
}

// https://docs.microsoft.com/en-us/azure/app-service/deploy-run-package#run-from-external-url-instead

output managedIdentityPrincipalID string = uami.properties.principalId
output appServiceName string = names.appService
output resourceNames object = names
