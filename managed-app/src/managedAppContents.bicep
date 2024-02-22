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

@description('Location for all resources.')
param location string

@description('The resource ID of the user-assigned managed identity.')
param userAssignedIdentityId string 

@description('The name of the KeyVault containing the metering information')
param runtimeKeyVaultName string

@description('The name of the metering secret')
param meteringSubmissionSecretName string

@description('The base URL for artifacts')
param _artifactsLocation string

@description('SAS token to access scripts etc.')
@secure()
param _artifactsLocationSasToken string

// param currentDateMarker string = utcNow('yyyy-MM-dd--HH-mm-ss')

var sample = {
  names: {
    nic: 'myVMNic'
    publicIPAddress: 'myPublicIP'
    vm: 'SimpleWinVM'
    virtualNetwork: 'MyVNET'
    subnet: 'Subnet'
    networkSecurityGroup: 'default-NSG'
    storageAccount: 'sto${uniqueString(resourceGroup().id)}'
  }
  addresses: {
    vnetCIDR: '10.0.0.0/16'
    subnetCIDR: '10.0.0.0/24'
  }
  vm: {
    auth: {
      passwordAuth: {
        computerName: 'vm'
        adminUsername: sshUsername
        adminPassword: sshPassword
      }
      sshAuth: {
        computerName: 'vm'
        adminUsername: sshUsername
        linuxConfiguration: {
          disablePasswordAuthentication: true
          ssh: {
            publicKeys: [
              {
                path: '/home/${sshUsername}/.ssh/authorized_keys'
                keyData: sshPublicKey
              }
            ]
          }
        }
      }
    }
  }
}

resource samplePublicIPAddressName 'Microsoft.Network/publicIPAddresses@2023-06-01' = {
  name: sample.names.publicIPAddress
  location: location
  properties: {
    publicIPAllocationMethod: 'Dynamic'
    dnsSettings: {
      domainNameLabel: dnsLabelPrefix
    }
  }
}

resource sampleNetworkSecurityGroup 'Microsoft.Network/networkSecurityGroups@2023-06-01' = {
  name: sample.names.networkSecurityGroup
  location: location
  properties: {
    securityRules: [
      {
        name: 'default-allow-22'
        properties: {
          priority: 1000
          access: 'Allow'
          direction: 'Inbound'
          destinationPortRange: '22'
          protocol: 'Tcp'
          sourcePortRange: '*'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

resource sampleVirtualNetwork 'Microsoft.Network/virtualNetworks@2023-06-01' = {
  name: sample.names.virtualNetwork
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        sample.addresses.vnetCIDR
      ]
    }
    subnets: [
      {
        name: sample.names.subnet
        properties: {
          addressPrefix: sample.addresses.subnetCIDR
          networkSecurityGroup: {
            id: sampleNetworkSecurityGroup.id
          }
        }
      }
    ]
  }
}

resource sampleNic 'Microsoft.Network/networkInterfaces@2023-06-01' = {
  name: sample.names.nic
  location: location
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          publicIPAddress: {
            id: samplePublicIPAddressName.id
          }
          subnet: {
            id: resourceId('Microsoft.Network/virtualNetworks/subnets', sample.names.virtualNetwork, sample.names.subnet)
          }
        }
      }
    ]
  }
  dependsOn: [
    sampleVirtualNetwork
  ]
}

resource sampleVm 'Microsoft.Compute/virtualMachines@2023-09-01' = {
  name: sample.names.vm
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityId}': {}
    }
  }
  tags: {
    // We attach the config as tags, so instance metadata can retrieve them
    runtimeKeyVaultName: runtimeKeyVaultName
    meteringSubmissionSecretName: meteringSubmissionSecretName
    uamiId: userAssignedIdentityId
  }
  properties: {
    hardwareProfile: {
      vmSize: vmSize
    }
    osProfile: ((authenticationType == 'password') ? sample.vm.auth.passwordAuth : sample.vm.auth.sshAuth)
    storageProfile: {
      osDisk: {
        createOption: 'FromImage'
      }
      imageReference: {
        publisher: 'canonical'
        offer: '0001-com-ubuntu-server-focal'
        sku: '20_04-lts-gen2'
        version: 'latest'
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: sampleNic.id
        }
      ]
    }
  }
}

var customScriptExtensionConfig = {
  directory: 'scripts'
  // the `script` will install the `meteringSubmissionScript` and metering config on the VM.
  script: 'localScript.sh'
  meteringSubmissionScript: 'submit-meter.sh'
}

resource customScriptExtension 'Microsoft.Compute/virtualMachines/extensions@2022-03-01' = {
  // https://github.com/Azure/custom-script-extension-linux
  name: 'customScriptExtensionLocalScript'
  parent: sampleVm
  location: location
  properties: {
    publisher: 'Microsoft.Azure.Extensions', type: 'CustomScript', typeHandlerVersion: '2.1'
    autoUpgradeMinorVersion: true
    // enableAutomaticUpgrade: bool
    // forceUpdateTag: 'string'
    protectedSettings: {
      fileUris: [ 
        uri(_artifactsLocation, '${customScriptExtensionConfig.directory}/${customScriptExtensionConfig.script}${_artifactsLocationSasToken}')
      ]
      commandToExecute: './${customScriptExtensionConfig.script} ${uri(_artifactsLocation, '${customScriptExtensionConfig.directory}/${customScriptExtensionConfig.meteringSubmissionScript}${_artifactsLocationSasToken}')}'
    }
  }
}

// https://docs.microsoft.com/en-us/azure/virtual-machines/extensions/custom-script-linux
// https://docs.microsoft.com/en-us/azure/virtual-machines/run-command-overview
// https://docs.microsoft.com/en-us/azure/virtual-machines/linux/run-command
// https://docs.microsoft.com/en-us/azure/virtual-machines/linux/run-command-managed
// https://docs.microsoft.com/en-us/azure/templates/microsoft.compute/virtualmachines/runcommands?tabs=bicep

output sshConnectionString string = '${sshUsername}@${reference(samplePublicIPAddressName.name).dnsSettings.fqdn}'
