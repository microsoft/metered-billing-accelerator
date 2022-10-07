# Sample managed app

This directory contains the code to create a deployment ZIP for an Azure Marketplace Managed Application.

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