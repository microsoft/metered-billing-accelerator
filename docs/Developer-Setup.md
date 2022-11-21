# Developer setup for the Metered Billing Accelerator

This document provides an overview to the local development setup of the metered billing accelerator. 

## Requirements

- Build environment for .NET Core 6
- An Azure Environment with
  - Event Hub (ideally with Event Hub Capture enabled)
  - Storage Account (for snapshot storage)
  - The credentials to access these services
- A valid offer in the Azure Marketplace (with configured billing dimensions), so that you can submit metering information for these. 
  - That could be either an Azure Managed App, or a SaaS offer. 
  - The credential to submit usage to marketplace.
  - Without a valid purchased offer/plan, you can still locally aggregate usage events, but the aggregator will not be able so submit the events to the billing API. In such a case, you will see values piling up in your state files, as the aggregator will receive error messages about non-existent subscriptions.

## Azure Environment

### Azure Credentials

Authentication to the Azure Services (Event Hubs, Event Hub Capture storage account, and the snapshot storage account) happens via **Azure AD authentication**. If you really want to use older authN mechanisms (storage account keys, or shared access signatures), you have to wire these up yourself; this is untested.

In a production environment, we would recommend the use of Azure Managed Identities, which you bind to your Azure compute instance. If you would like to simulate Azure Managed Identity locally on your laptop (have a service listening on `169.254.169.254` which issues tokens), you might check out the [chgeuer/*azure_instance_metadata_service_simulator* (A small utility to simulate the Azure instance metadata endpoint on a developer's laptop)](https://github.com/chgeuer/azure_instance_metadata_service_simulator).

Otherwise, you can use an old-fashioned Azure AD service principal credential (with a secret). 

#### Infrastructure Credentials

The solution needs to be able to access Azure EventHub and Azure Storage. You can use either a single service principal credential for accessing these two services, or a managed identity. 

- For using a service principal, set the `AZURE_METERING_INFRA_CLIENT_ID`, `AZURE_METERING_INFRA_CLIENT_SECRET` and `AZURE_METERING_INFRA_TENANT_ID` environment variables.
- For managed identity, just don't set these values.

> Partial settings (setting only one or two out of the `*_CLIENT_ID/*_CLIENT_SECRET/*_TENANT_ID` tuple will result in the application not starting. You have to **set all three** (for service principal), **or none** (for managed identity).

### Azure Marketplace API credential (to submit metering values to Azure)

In order to submit values to the Azure marketplace metered billing API, you also need a credential. 

- For a SaaS solution, you **must** use a service principal credential.
- For an Azure Managed Application, you can either use the managed app's managed identity (if you choose to emit usage out of the managed app itself), or you use a service principal (for example if you centrally want to aggregate and emit usage for all managed apps).

Similar to the previously described infrastructure credential, you either **set all three** `AZURE_METERING_MARKETPLACE_CLIENT_ID`, `AZURE_METERING_MARKETPLACE_CLIENT_SECRET` and `AZURE_METERING_MARKETPLACE_TENANT_ID`, or none. The `AZURE_METERING_MARKETPLACE_TENANT_ID` would be the Azure AD tenant ID of the ISV/publisher tenant.

### Infrstructure

A quick way to get the underlying infrastructure provisioned is the `deploy/arm-legacy/templates/infra/dev.bicep` script. 

### Event Hubs

We need are the Event Hubs instance, through which our event stream flows. The applications which generate usage information publish to that event hub. The metered billing accelerator both reads and writes to that event hub.

Ideally, you should configure that event hubs instance with event hub capture enabled. 

The 