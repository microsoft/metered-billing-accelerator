# Installation

The simplest way to deploy this solution is by using the bicep files located in this folder.

## Prerequisites

1. Installed Bicep.

      Follow the instructions in the [Deployment environment](https://docs.microsoft.com/en-us/azure/azure-resource-manager/bicep/install#deployment-environment) section and install Bicep as part of your Azure CLI, Azure PowerShell or as a standalone package.

      In the instructions below we will use the Azure CLI.

2. [Azure Marketplace Offer](https://docs.microsoft.com/en-us/azure/marketplace/azure-app-offer-setup) SaaS or Managed Service offer, and the Azure Active Directory application credentials.

## Deployment

The entire solution can be deployed with a single command. You have two options:

1. Deploy the solution into new resource group that will be created by the deployment

      > [!NOTE]
      > Change the values starting with *$* with your own parameter values.

```azurecli
az deployment sub create -f main-new-rg.bicep \
      --location westeurope \
      --parameters rgLocation=westeurope \
      --parameters appNamePrefix=$meteredbilling \
      --parameters ADApplicationID=$CHANGE-WITH-CLIENT_ID \
      --parameters ADApplicationSecret=$CHANGE-WITH-CLIENT_SECRET \
      --parameters tenantID=$CHANGE-WITH-TENANT_ID
```

2. Deploy the solution into an existing resource group:

```azurecli
az deployment group create --template-file main-existingRG.bicep  \
      --parameters appNamePrefix=$meteredbilling \
      --resource-group $meteredbilling-rg \
      --parameters ADApplicationID=$CHANGE-WITH-CLIENT_ID \
      --parameters ADApplicationSecret=$CHANGE-WITH-CLIENT_SECRET \
      --parameters tenantID=$CHANGE-WITH-TENANT_ID
```

## Parameters

| Parameter | Description |
|-----------| -------------|
| appNamePrefix | A unique prefix used for creating the deployment and applications. Example: contoso |
| TenantID | The value should match the value provided for Active Directory TenantID in the Technical Configuration of the Transactable Offer in Partner Center |
| ADApplicationID | The value should match the value provided for Active Directory Single-Tenant Application ID in the Technical Configuration of the Transactable Offer in Partner Center |
| ADApplicationSecret | Secret key of the AD Application |

## Usage

After the deployment you can start sending metering events to the EventHub.

To understand the messages sent from the client pleas navigate to [Client messages](metered-billing-accelerator#client-messages) section.

To configure your producer take the eventHubName and eventHubConnectionString values from the output of the deployment.
