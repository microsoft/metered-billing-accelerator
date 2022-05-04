# Instalation

The simplest way to deploy this solution is by using the bicep fiels located in this folder.

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
      --parameters appNamePrefix=$meteredbilling \
      --location westeurope \
      --parameters AZURE_METERING_MARKETPLACE_CLIENT_ID=$CHANGE-WITH-CLIENT_ID \
      --parameters AZURE_METERING_MARKETPLACE_CLIENT_SECRET=$CHANGE-WITH-CLIENT_SECRET \
      --parameters AZURE_METERING_MARKETPLACE_TENANT_ID=$CHANGE-WITH-TENANT_ID \
``` 
2. Deploy the solution into an existing resource group:
```azurecli
az deployment group create --template-file main-existingRG.bicep  \
      --parameters appNamePrefix=$meteredbilling \
      --resource-group $meteredbilling-rg \
      --parameters AZURE_METERING_MARKETPLACE_CLIENT_ID=$CHANGE-WITH-CLIENT_ID \
      --parameters AZURE_METERING_MARKETPLACE_CLIENT_SECRET=$CHANGE-WITH-CLIENT_SECRET \
      --parameters AZURE_METERING_MARKETPLACE_TENANT_ID=$CHANGE-WITH-TENANT_ID \
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

To understund the messages sent from the client pleas navigate to [Client messages](metered-billing-accelerator#client-messages) section.

### Example script in python:

Take the eventHubName and eventHubConnectionString values from the output of the deployment.

```python
import asyncio
from azure.eventhub.aio import EventHubProducerClient
from azure.eventhub import EventData

eventHubConnectionString = 'CHANGE THIS VALUE WITH THE DEPLOYMENT OUTPUT VALUE'
eventHubName = 'CHANGE THIS VALUE WITH THE DEPLOYMENT OUTPUT VALUE'

# Create a producer client to send messages to the event hub.
# Specify a connection string to your event hubs namespace and
# the event hub name.
producer = EventHubProducerClient.from_connection_string(conn_str=eventHubConnectionString, eventhub_name=eventHubName)

async def init():
    
    async with producer:
      # Create a batch.
      event_data_batch = await producer.create_batch()

      # Add events to the batch.
      event_data_batch.add(EventData("""{
                                          "type":"SubscriptionPurchased",
                                          "value":{
                                          "subscription":{
                                          "scope":"XXXXXX-MARKETPLACE-SUBSCRIPTION-ID",
                                          "subscriptionStart":"2022-04-08T08:45:20Z",
                                          "renewalInterval":"Monthly",
                                          "plan":{
                                                "planId":"gold_plan_id",
                                                "billingDimensions": {
                                                "user_id": { "monthly": 10, "annually": 0 },
                                                }
                                          }
                                          },
                                          "metersMapping":{
                                          "user_id": "user_id"
                                          }
                                          }
                                          } """))
      
      event_data_batch.add(EventData("""{
                                    "type": "UsageReported",
                                    "value": {
                                          "internalResourceId": "XXXXXX-MARKETPLACE-SUBSCRIPTION-ID",
                                          "timestamp":          "2022-04-08T08:45:20Z",
                                          "meterName":          "user_id",
                                          "quantity":           1
                                    }
                                    } """))


        # Send the batch of events to the event hub.
        await producer.send_batch(event_data_batch)

      
loop = asyncio.get_event_loop()
loop.run_until_complete(init())

```