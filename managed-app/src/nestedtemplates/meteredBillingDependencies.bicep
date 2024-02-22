@description('Location for all resources.')
param location string

@description('Location for scripts etc.')
param _artifactsLocation string

@description('SAS token to access scripts etc.')
@secure()
param _artifactsLocationSasToken string

@description('The bootstrap secret to request service principal creation')
@secure()
param bootstrapSecretValue string

@description('The metering configuration object')
param meteringConfiguration object = {
  servicePrincipalCreationURL: 'https://my-rest-api.contoso.com'
  amqpEndpoint: 'https://metering-contoso.servicebus.windows.net/metering'
  publisherVault: {
    publisherSubscription: '{isvSubscriptionId}'
    vaultResourceGroupName: '...'
    vaultName: '...'
    bootstrapSecretName: 'BootstrapSecret'
  }
}

param currentDateMarker string = utcNow('yyyy-MM-dd--HH-mm-ss')

var names = {
  identity: {
    setup: 'uami-setup'
    runtime: 'uami-runtime'
  }
  runtimeKeyVault: {
    name: 'kvchgp${uniqueString(resourceGroup().id)}'
    meteringSubmissionSecretName: 'meteringsubmissionconnection'
  }
  deploymentScript: {
    name: 'deploymentScriptCreateSP--${currentDateMarker}'
    azCliVersion: '2.36.0'
    scriptName: 'scripts/triggerServicePrincipalCreation.sh'
  }
  managedApp: {
    managedBy: resourceGroup().managedBy
    resourceGroupName: split(resourceGroup().managedBy, '/')[4]
    appName: split(resourceGroup().managedBy, '/')[8]
  }
}

var roles = {
  Owner: '8e3af657-a8ff-443c-a75c-2fe8c4bcb635'
  Contributor: 'b24988ac-6180-42a0-ab88-20f7382dd24c'
  Reader: 'acdd72a7-3385-48ef-bd42-f606fba81ae7'
  KeyVault: {
    // KeyVaultAdministrator: '00482a5a-887f-4fb3-b363-3b7fe8e74483'
    // KeyVaultCertificatesOfficer: 'a4417e6f-fecd-4de8-b567-7b0420556985'
    // KeyVaultCryptoOfficer: '14b46e9e-c2b7-41b4-b07b-48a6ebf60603'
    // KeyVaultCryptoServiceEncryptionUser: 'e147488a-f6f5-4113-8e2d-b22465e65bf6'
    // KeyVaultCryptoUser: '12338af0-0e69-4776-bea7-57ae8d297424'
    // KeyVaultReader: '21090545-7ca7-4776-b22c-e363652d74d2'
    KeyVaultSecretsOfficer: 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
    KeyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  }
}

// Will be used by the deploymentScript to do all setup work
resource setupIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: names.identity.setup
  location: location
}


// Will be attached to compute resources which submit metering information,
// and therefore need to be able to retrieve the connection string from KeyVault
resource runtimeIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: names.identity.runtime
  location: location
}

// Grant setupIdentity Contributor perms on the managed resource group.
resource setupIdentityIsContributorOnManagedResourceGroup 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(setupIdentity.id, roles.Contributor, resourceGroup().id)
  scope: resourceGroup()
  properties: {
    description: '${setupIdentity.name} should be Contributor on the managed resource group'
    principalType: 'ServicePrincipal'
    principalId: reference(setupIdentity.id, '2023-01-31').principalId
    delegatedManagedIdentityResourceId: setupIdentity.id
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.Contributor)    
  }
}

// Grant the setupIdentity Reader permissions on the managed app object
module setupIdentityIsReaderOnManagedAppObject 'permissionOnManagedAppDeployment.bicep' = {
  name: 'permissionOnManagedAppDeployment'
  scope: resourceGroup(names.managedApp.resourceGroupName)
  params: {
    name: '${names.managedApp.appName}/Microsoft.Authorization/${guid(setupIdentity.id, roles.Reader, resourceGroup().id, resourceGroup().managedBy)}'
    properties: {
      scope: resourceGroup().managedBy
      description: '${setupIdentity.name} should be Reader on the managed app object'
      principalType: 'ServicePrincipal'
      principalId: reference(setupIdentity.id, '2023-01-31').principalId
      delegatedManagedIdentityResourceId: setupIdentity.id
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.Reader)
    }
  }
}

resource runtimeKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: names.runtimeKeyVault.name
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: false
    networkAcls: {
       bypass: 'AzureServices'
       defaultAction: 'Allow'
    }
  }  
}

/* Is it Microsoft.Resources/deploymentScripts@2020-10-01 or Microsoft.Resources/deploymentScripts@2023-01-31? 
   Bicep seems not to know Microsoft.Resources/deploymentScripts@2023-01-31, 
   but https://learn.microsoft.com/en-us/azure/azure-resource-manager/templates/deployment-script-template#access-private-virtual-network says it exists
   and ARM TTK complains when using 2020-10-01, because "Api versions must be the latest or under 2 years old (730 days)"
*/
resource deploymentScript 'Microsoft.Resources/deploymentScripts@2023-08-01' = { 
  name: names.deploymentScript.name
  location: location
  kind: 'AzureCLI'
  properties: {
    azCliVersion: names.deploymentScript.azCliVersion
    timeout: 'PT10M'
    retentionInterval: 'P1D'
    cleanupPreference: 'OnExpiration'
    containerSettings: {
      containerGroupName: uniqueString(resourceGroup().id, names.deploymentScript.name)
    }
    primaryScriptUri: uri(_artifactsLocation, '${names.deploymentScript.scriptName}${_artifactsLocationSasToken}')
    environmentVariables: [
      { name: 'SERVICE_PRINCIPAL_CREATION_URL',      value:       meteringConfiguration.servicePrincipalCreationURL  }
      { name: 'BOOTSTRAP_SECRET_VALUE',              secureValue: bootstrapSecretValue                               }
      { name: 'MANAGED_BY',                          value:       names.managedApp.managedBy                         }
    ]
  }
}

resource publisherKeyVaultWithBootstrapSecret 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: meteringConfiguration.publisherVault.vaultName
  scope: resourceGroup(meteringConfiguration.publisherVault.publisherSubscription, meteringConfiguration.publisherVault.vaultResourceGroupName)
}

module setServicePrincipalSecret 'setSecret.bicep' = {
  // Take the secretName output from the deploymentScript, 
  // fetch the actual service principal credential from the publisher KeyVault, 
  // and store it in the runtime KeyVault.
  name: 'setServicePrincipalSecret'
  params: {
    vaultName: runtimeKeyVault.name
    secretName: names.runtimeKeyVault.meteringSubmissionSecretName
    secretValue: publisherKeyVaultWithBootstrapSecret.getSecret(reference(deploymentScript.id).outputs.secretName)
    amqpConnectionString: meteringConfiguration.amqpEndpoint
    managedBy: names.managedApp.managedBy
  }
}

resource runtimeIdentityCanReadMeteringSubmissionSecretPrincipalId 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(runtimeIdentity.id, roles.KeyVault.KeyVaultSecretsUser, runtimeKeyVault.id)  
  scope: runtimeKeyVault
  properties: {
    description: '${runtimeIdentity.name} should be a KeyVaultSecretsUser on the ${runtimeKeyVault.id}'
    principalId: runtimeIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.KeyVault.KeyVaultSecretsUser)
    delegatedManagedIdentityResourceId: runtimeIdentity.id
  }
}

output setupIdentityId string = setupIdentity.id
output runtimeIdentityId string = runtimeIdentity.id
output runtimeKeyVaultName string = names.runtimeKeyVault.name
output meteringSubmissionSecretName string = names.runtimeKeyVault.meteringSubmissionSecretName
// Do not expose the service principal secret in an output, otherwise the customer could see it by looking at deployment operations
// output keyVaultSecret object = reference(deploymentScript.name).outputs.keyVaultSecret
