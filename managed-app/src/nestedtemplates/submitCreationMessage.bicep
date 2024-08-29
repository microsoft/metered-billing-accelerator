@description('Location for all resources.')
param location string

@description('Location for scripts etc.')
param _artifactsLocation string

@description('SAS token to access scripts etc.')
@secure()
param _artifactsLocationSasToken string

param runtimeIdentityId string

@description('The JSON representation of the app\'s plan')
param planConfiguration object

param runtimeKeyVaultName string

param meteringSubmissionSecretName string

param currentDateMarker string = utcNow('yyyy-MM-dd--HH-mm-ss')

var names = {
  deploymentScript: {
    name: 'deploymentScriptSubmitInitialMessage-${currentDateMarker}'
    azCliVersion: '2.36.0'
    scriptName: 'scripts/submitCreationMessage.sh'
  }
}

resource deploymentScript 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
  name: names.deploymentScript.name
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${runtimeIdentityId}': {}
    }
  }
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
      { name: 'RUNTIME_IDENTITY',                value: runtimeIdentityId }
      { name: 'METERING_PLAN_JSON',              value: string(planConfiguration) }
      { name: 'RUNTIME_KEYVAULT_NAME',           value: runtimeKeyVaultName }
      { name: 'METERING_SUBMISSION_SECRET_NAME', value: meteringSubmissionSecretName }
    ]
  }
}

output aio object = {
  location: location
  _artifactsLocation: _artifactsLocation
  planConfiguration: planConfiguration
  script: reference(deploymentScript.id, '2023-08-01', 'Full')
}
