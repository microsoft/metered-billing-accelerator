# Sample managed app

This directory contains the code to create a deployment ZIP for an Azure Marketplace Managed Application. 

- The ARM templates in the ZIP are created out of Bicep templates.
- The [`build.sh`](build.sh) script runs the overall build process.
- **Composability**: the overall managed app will be composed of 3 parts. 
  - **The actual app:** The actual contents of the managed app are in [`src/managedAppContents.bicep`](managed-app/src/managedAppContents.bicep). 
    - This Bicep template contains a demo workload, consisting of a virtual machine and corresponding components.
  - **The metered billing dependencies: ** 
    - The [`src/nestedtemplates`](src/nestedtemplates) and [`src/scripts`](src/scripts) directories contain templates and scripts for supporting metered billing. In particular, these pull in service principal credentials from the ISV/publisher, and store them in a KeyVault in the managed resource group.
  - **The main() entry point:** The Bicep template [`src/mainTemplate.bicep`](src/mainTemplate.bicep) is the entry point which composes together the actual app and the metered-billing dependencies.
    - The [`src/mainTemplate.bicep`](src/mainTemplate.bicep) must pull in all parameters necessary by the inner actual app, and forward them to [`src/managedAppContents.bicep`](managed-app/src/managedAppContents.bicep).

## Configuring and Building

### Configure the Customer usage attribution ID

Edit the file [`customer_usage_attribution_ID.json`](customer_usage_attribution_ID.json) and add the "Customer usage attribution ID" from your plan's Technical Configuration. It's a JSON object which must contain an `id` value, like this:

```json
{
	"id": "pid-2bed8b3a-df10-4f7d-b3e0-9264d1252055-partnercenter"
}
```

This is usually a GUID like `pid-<<someguid>>-partnercenter`, which you can find in the partner portal's technical configuration of your plan (see also the [docs](https://docs.microsoft.com/en-us/azure/marketplace/azure-partner-customer-usage-attribution)).

### Configure the `meteringConfiguration.json` 

The [`meteringConfiguration.json` ](meteringConfiguration.json) file contains the pointers to your metering backend:

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

  - The `servicePrincipalCreationURL` contains the link to your REST API to request creation of service principals. This is the base URL of the place where you deployed your running copy of the [`Metering.SharedResourceBroker`](../src/Metering.SharedResourceBroker). 
    - The [`src/scripts/triggerServicePrincipalCreation.sh`](src/scripts/triggerServicePrincipalCreation.sh) script leverages that URL to talk to the `${servicePrincipalCreationURL}/CreateServicePrincipalInKeyVault` endpoint, to create a service principal.

  - The `publisherSubscription`, `vaultResourceGroupName` and `vaultName` point to the ISV/publisher KeyVault that contains the bootstrap secret for creating the service principal. The `bootstrapSecretName` is the name of the secret necessary to authenticate calls to the `${servicePrincipalCreationURL}/CreateServicePrincipalInKeyVault`.
  - The `amqpEndpoint` points to the publisher's EventHub, where the managed app should send metering events to.

### Configure the marketplace details of the managed app via `plan.json`

The [`plan.json`](plan.json) file contains the JSON representation of the the Azure Marketplace offer's plan. 