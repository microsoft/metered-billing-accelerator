param vaultName string

param secretName string 

@description('The service principal secret')
@secure()
param secretValue string

@description('The .managedBy property of the managed resource group')
param managedBy string

@description('The EventHubs endpoint')
param amqpConnectionString string

var servicePrincipal = {
	servicePrincipalInformation: json(secretValue)
}

var connectionInformation = {
	servicePrincipalInformation: {
		meteringEventHub: amqpConnectionString
	}
	managedBy: managedBy
}

var mergedSecrets = string(union(servicePrincipal, connectionInformation))

resource runtimeKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: vaultName
}

resource meteringSubmissionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
	parent: runtimeKeyVault
	name: secretName
	properties: {
	  value: mergedSecrets
	}
}
