@description('Location for all resources.')
param location string = resourceGroup().location

@description('These credentials can be used to remotely access the cluster.')
param sshUsername string

@allowed([
  'password'
  'sshPublicKey'
])
@description('Type of authentication to use on the Virtual Machine.')
param authenticationType string = 'password'

@description('These credentials can be used to remotely access the cluster.')
@secure()
param sshPassword string = ''

@description('SSH key for the Virtual Machine.')
param sshPublicKey string = ''

@description('Unique DNS Name for the Public IP used to access the Virtual Machine.')
@minLength(3)
@maxLength(24)
param dnsLabelPrefix string

@description('Size of the virtual machine.')
param vmSize string = 'Standard_D2_v5'

// https://github.com/Azure/azure-quickstart-templates/blob/master/1-CONTRIBUTION-GUIDE/best-practices.md#deployment-artifacts-nested-templates-scripts
@description('The base URI where artifacts required by this template are located including a trailing \'/\'')
param _artifactsLocation string = deployment().properties.templateLink.uri

@description('The sasToken required to access _artifactsLocation.  When the template is deployed using the accompanying scripts, a sasToken will be automatically generated. Use the defaultValue if the staging location is not secured.')
@secure()
param _artifactsLocationSasToken string = ''

var meteringConfiguration = loadJsonContent('../meteringConfiguration.json')
// var customerUsageAttribution = loadJsonContent('../customer_usage_attribution_ID.json')

// This is an empty deployment which augments the managed app resource group with the usage attribution ID
module THIS_IS_INVALID_PLEASE_RUN_buildsh './nestedtemplates/emptyFile.bicep' = {
  // name: customerUsageAttribution.id
  name: 'THIS-IS-INVALID-PLEASE-RUN-buildsh'
  params: {}
}

resource publisherKeyVaultWithBootstrapSecret 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: meteringConfiguration.publisherVault.vaultName
  scope: resourceGroup(meteringConfiguration.publisherVault.publisherSubscription, meteringConfiguration.publisherVault.vaultResourceGroupName)
}

// The required code for setting up metering
module setupMeteredBillingConfigurationModule './nestedtemplates/meteredBillingDependencies.bicep' = {
  name: 'setupMeteredBillingConfiguration'
  params: {
    location: location
    _artifactsLocation: _artifactsLocation
    _artifactsLocationSasToken: _artifactsLocationSasToken
    bootstrapSecretValue: publisherKeyVaultWithBootstrapSecret.getSecret(meteringConfiguration.publisherVault.bootstrapSecretName)
    meteringConfiguration: meteringConfiguration
  }
}

// resource runtimeKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
//   name: setupMeteredBillingConfigurationModule.outputs.runtimeKeyVaultName
//   scope: resourceGroup()
// }

module submitInitialMeteringMessage './nestedtemplates/submitCreationMessage.bicep' = {
  name: 'submitInitialMeteringMessage'
  params: {
    location: location
    _artifactsLocation: _artifactsLocation
    _artifactsLocationSasToken: _artifactsLocationSasToken
    planConfiguration: loadJsonContent('../plan.json')
    runtimeIdentityId: setupMeteredBillingConfigurationModule.outputs.runtimeIdentityId
    // runtimeIdentityId: setupMeteredBillingConfigurationModule.outputs.setupIdentityId
    runtimeKeyVaultName: setupMeteredBillingConfigurationModule.outputs.runtimeKeyVaultName
    meteringSubmissionSecretName: setupMeteredBillingConfigurationModule.outputs.meteringSubmissionSecretName
  }
}

//
// managedAppContents.bicep contains the actual managed app system, i.e. the ISV/publisher's solution.
//
module managedAppContents './managedAppContents.bicep' = {
  name: 'managedAppContents'
  params: {
    location: location
    _artifactsLocation: _artifactsLocation
    _artifactsLocationSasToken: _artifactsLocationSasToken
    vmSize: vmSize
    dnsLabelPrefix: dnsLabelPrefix
    sshUsername: sshUsername, authenticationType: authenticationType, sshPassword: sshPassword, sshPublicKey: sshPublicKey
    userAssignedIdentityId: setupMeteredBillingConfigurationModule.outputs.runtimeIdentityId
    runtimeKeyVaultName: setupMeteredBillingConfigurationModule.outputs.runtimeKeyVaultName
    meteringSubmissionSecretName: setupMeteredBillingConfigurationModule.outputs.meteringSubmissionSecretName
  }
}

output runtimeIdentityId string = setupMeteredBillingConfigurationModule.outputs.runtimeIdentityId
output runtimeKeyVaultName string = '${setupMeteredBillingConfigurationModule.outputs.runtimeKeyVaultName}/${setupMeteredBillingConfigurationModule.outputs.meteringSubmissionSecretName}'
//output systemAssignedManagedIdentity string = managedIdentity
output sshConnectionString string = managedAppContents.outputs.sshConnectionString
