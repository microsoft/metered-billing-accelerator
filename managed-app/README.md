# Sample managed app

This directory contains the code to create a deployment ZIP for an Azure Marketplace Managed Application. 

- The ARM templates in the ZIP are created out of Bicep templates.
- The [`managed-app/build.sh`](build.sh) script runs the overall build process.
- **Composability**: the overall managed app will be composed of 3 parts. 
  - **The actual app:** The actual contents of the managed app are in `managed-app/src/managedAppContents.bicep`. 
    - This Bicep template contains a demo workload, consisting of a virtual machine and corresponding components.
  - **The metered billing dependencies: ** 
    - The [`managed-app/src/nestedtemplates`](src/nestedtemplates) and [`managed-app/src/scripts`](src/scripts) directories contain templates and scripts for supporting metered billing. In particular, these pull in service principal credentials from the ISV/publisher, and store them in a KeyVault in the managed resource group.
  - **The main() entry point:** The Bicep template [`managed-app/src/mainTemplate.bicep`](src/mainTemplate.bicep) is the entry point which composes together the actual app and the metered-billing dependencies.

## Configuring and Building

### Configure the Customer usage attribution ID

Edit the file [`customer_usage_attribution_ID.json`](./customer_usage_attribution_ID.json) and add the "Customer usage attribution ID" from your plan's Technical Configuration. It should look like this:

```json
{
	"id": "pid-2bed8b3a-df10-4f7d-b3e0-9264d1252055-partnercenter"
}
```

### Configure the `meteringConfiguration.json` 

This file contains the pointers to your metering backend:

```json
{
  "servicePrincipalCreationURL": "https://notifications.demo-chgeuer.azureisv.com",
  "publisherVault": {
    "publisherSubscription": "706df49f-998b-40ec-aed3-7f0ce9c67759",
    "vaultResourceGroupName": "backend-2022-07-15-2",
    "vaultName": "SRBKVchgp20220715-2",
    "bootstrapSecretName": "BootstrapSecret"
  },
  "amqpEndpoint": "https://metering-20220825.servicebus.windows.net/metering"
}
```

  - The `servicePrincipalCreationURL` contains the link to your REST API to request creation of service principals.
  - The `publisherSubscription`, `vaultResourceGroupName` and `vaultName` point to the ISV/publisher KeyVault that contains the bootstrap secret for creating the service principal. The `bootstrapSecretName` is the name of the secret.
  - The `amqpEndpoint` points to the publisher's EventHub where metering events are ingested.

### Configure the marketplace details of the managed app via `plan.json`

The `plan.json` file contains the marketplace plan. 