# README: The metering-accelerator

## tl;dr

> This component takes care of the accounting necessary for correctly reporting custom metering information to the Azure Marketplace Metering API. 

## Design goals

- Allow the application developer to submit all relevant usage events via a light-weight SDK, and not worry about the intricacies of Azure Marketplace Metering.
- Support all current (January 2022) Azure Marketplace offer types which can support custom meters, i.e. both Azure Managed Applications, as well as Software-as-a-Service. 
- Main audience are 'small' ISVs, i.e. software companies which intend to bring a new offering into Azure Marketplace, but who might not yet have existing massive investments into billing and accounting infrastructure. 
- The hosting costs should be minimal, preferably no premium services like CosmosDB etc..
- Robust and resilient: The solution should easily hum along in presence of failures. If a component fails, it'll catch up the next time it runs. Just make sure the compute component runs a few times a day.
- Hosting-agnostic: The compute component should be able to be hosted on a broad variety of compute paradigms, such as slapping it onto already existing virtual machine, or into an Azure function, a Docker container in ACI or K8s, or an on-premises Raspberry Pi.
- Just do the right thing (TM).
- Support various authentication mechanisms, i.e. both service principals, as well managed identity.
- Capture rich historic information, to enable future scenarios, such as analytics for customer retention, usage statistics, etc.
- Capture the full history of the system state, i.e. allow event sourcing.
- Have a 'comparably' lightweight client-SDK, i.e. rely on supported Azure SDKs for the actual heavy-lifting.
- JSON for both messages, as well as for state representation.

## Configuration via environment variables

### Azure Marketplace API credential (to submit metering values to Azure)

- For a SaaS solution, you must use a service principal credential, ie. you must set the values
  - `AZURE_METERING_MARKETPLACE_CLIENT_ID` and `AZURE_METERING_MARKETPLACE_CLIENT_SECRET`: These are the ones you configured in the Marketplace partner portal, alongside with your
  - `AZURE_METERING_MARKETPLACE_TENANT_ID`: This is the Azure AD tenant ID of your ISV tenant
- For a managed application, you will use the managed app's managed identity, i.e. you must NOT set these `AZURE_METERING_MARKETPLACE_...` values. If these are missing, the solution uses Managed Identity.

### Infrastructure Credentials

The solution needs to be able to access Azure EventHub and Azure Storage. You can use either a service principal credential for both, or managed identity for both. 

- For service principal, set `AZURE_METERING_INFRA_CLIENT_ID`, `AZURE_METERING_INFRA_CLIENT_SECRET` and `AZURE_METERING_INFRA_TENANT_ID`.
- For managed identity, just don't set these values.

> Partial settings (setting only one or two out of the `*_CLIENT_ID/*_CLIENT_SECRET/*_TENANT_ID` tuple will result in the application not starting. You have to set all three (for service principal), or none (for managed identity).

### Endpoints

Configure the appropriate endpoints for EventHub and Storage via environment variables:

- EventHub
  - Set `AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME` and `AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME` with the *pure* names, like `meteringhack` and `hub1`.
  - The eventhub namespace name is the  first part of the hostname in the connection string `Endpoint=sb://meteringhack.servicebus.windows.net/`.
  - The instance name corresponds to the  connection string's `EntityPath=hub1`.
- Storage
  - Set `AZURE_METERING_INFRA_CHECKPOINTS_CONTAINER` to the checkpoints container's URL  (will be used by the EventHub SDK's `EventProcessorClient`) 
  - Set `AZURE_METERING_INFRA_SNAPSHOTS_CONTAINER`, where the solution stores the aggregator state. **This is the long-term database of the system!!!**
  - Set `AZURE_METERING_INFRA_CAPTURE_CONTAINER` for the ability to read through EventHub capture.
  - Set `AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT` to the proper format of the blobs in the capture container, something like `{Namespace}/{EventHub}/p{PartitionId}--{Year}-{Month}-{Day}--{Hour}-{Minute}-{Second}`, namely the value from the EventHub'r ARM configuration, `archiveDescription.destination.properties.archiveNameFormat`. 
    Check the [documentation](https://docs.microsoft.com/en-us/azure/event-hubs/event-hubs-resource-manager-namespace-event-hub-enable-capture#capturenameformat) for details

### Local dev setup

For local development, set a few environment variables. On Windows, you can set the local user's environment variables with this script:

```cmd
setx.exe AZURE_METERING_MARKETPLACE_CLIENT_ID             deadbeef-1234-45f7-ae17-741d339bb986
setx.exe AZURE_METERING_MARKETPLACE_CLIENT_SECRET         sitrsneit239487234~nienienieni-ULYNE
setx.exe AZURE_METERING_MARKETPLACE_TENANT_ID             foobar.onmicrosoft.com

setx.exe AZURE_METERING_INFRA_CLIENT_ID                   deadbeef-1234-4aef-80d1-83b052e58b42
setx.exe AZURE_METERING_INFRA_CLIENT_SECRET               sitrsneit239487234~nienienieni-ULYNE
setx.exe AZURE_METERING_INFRA_TENANT_ID                   foobar.onmicrosoft.com

setx.exe AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME      meteringhack-standard
setx.exe AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME       hub2

setx.exe AZURE_METERING_INFRA_CHECKPOINTS_CONTAINER       https://meteringhack.blob.core.windows.net/checkpoint
setx.exe AZURE_METERING_INFRA_SNAPSHOTS_CONTAINER         https://meteringhack.blob.core.windows.net/snapshots

setx.exe AZURE_METERING_INFRA_CAPTURE_CONTAINER           https://meteringhack.blob.core.windows.net/hub2capture
setx.exe AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT     {Namespace}/{EventHub}/p{PartitionId}--{Year}-{Month}-{Day}--{Hour}-{Minute}-{Second}

setx.exe WSLENV AZURE_METERING_MARKETPLACE_CLIENT_ID/u:AZURE_METERING_MARKETPLACE_CLIENT_SECRET/u:AZURE_METERING_MARKETPLACE_TENANT_ID/u:AZURE_METERING_INFRA_CLIENT_ID/u:AZURE_METERING_INFRA_CLIENT_SECRET/u:AZURE_METERING_INFRA_TENANT_ID/u:AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME/u:AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME/u:AZURE_METERING_INFRA_CHECKPOINTS_CONTAINER/u:AZURE_METERING_INFRA_SNAPSHOTS_CONTAINER/u:AZURE_METERING_INFRA_CAPTURE_CONTAINER/u:AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT/u:
```

## Demo time - How to run / try it yourself

The service principal `AZURE_METERING_INFRA_CLIENT_ID` must have access (read/write to both the `AZURE_METERING_INFRA_EVENTHUB` event hub, as well as the checkpoints blob storage, the snapshot blob storage, and read access to the eventhub capture blob storage).

There are currently two CLI console apps to try it out:

- The `Demos\EventHubDemo` is the aggregator. 
  - It aggregates events, and displays stuff on screen. You can't interact with it. 
  - When you stop it, and restart it, it might take up to a minute until you see output, as it's battling leadership election games with it's previous incarnation on who owns which EventHub partition.
- The `Demos\SimpleMeteringSubmissionClient` is your demo app to submit real values.
  - Before you can see anything usefull, you must create a subscription: Type `c 1` for example, which (c)reates a new subscription. The code takes the `1`, and generates a GUID from it. 
    So unfortunately you can't see that `1` in the `Demos\EventHubDemo`, only the corresponding GUID. 
  - You can create multiple subscriptions, e.g. type `c 2` and `c 3`, and create more subs to play with
  - After you have a subscription, you can (s)ubmit metering values, by saying `s 1 20`, which sends a usage event of 20 units for the subscription GUID which corresponds to the `1`.
  - Once you have pressed the return button, an event goes into EventHub, and you should see metering values on the `Demos\EventHubDemo` side change.

### Example

Run these commands

- `c foo`: This (c)reates a subscription. The demo app converts the string `foo` into a GUID `b5c7ee0b-3fea-db0f-c95d-0dd47f3c5bc2`, and submits an event creating a new subscription.
- `s foo 99`: This submits the consumption to the subscription `b5c7ee0b-3fea-db0f-c95d-0dd47f3c5bc2`
- `s foo 1000`: This submits the consumption to the subscription `b5c7ee0b-3fea-db0f-c95d-0dd47f3c5bc2`
- `s foo 10000`: This submits the consumption to the subscription `b5c7ee0b-3fea-db0f-c95d-0dd47f3c5bc2`

Somewhere in the output, you will see this (at different spots):

```
 0 b5c7ee0b-3fea-db0f-c95d-0dd47f3c5bc2:   cpucharge: Remaining 1000/10000 (month/year)
 0 b5c7ee0b-3fea-db0f-c95d-0dd47f3c5bc2:   cpucharge: Remaining 901/10000 (month/year)
 0 b5c7ee0b-3fea-db0f-c95d-0dd47f3c5bc2:   cpucharge: Remaining 9901 (year)
 0 b5c7ee0b-3fea-db0f-c95d-0dd47f3c5bc2:   cpucharge: 99 consumed
```

When the subscription was created (check the `plan.json` file in the `SimpleMeteringSubmissionClient` directory), these details were included in the plan:

```json
    {
      "dimension": "cpucharge",
      "name": "Per CPU Connected",
      "unitOfMeasure": "cpu/hour",
      "includedQuantity": {
        "monthly": "1000",
        "annually": "10000"
    }
```

You can see an included annual quantity of 10000 units, and a monthly quantity of 1000. This is reflected in the output:

```text
 0 b5c7ee0b-3fea-db0f-c95d-0dd47f3c5bc2:   cpucharge: Remaining 1000/10000 (month/year)
```

- Once you (s)ubmit a usage of 99 units (by typing `s foo 99`), the `Remaining 1000/10000 (month/year)` become `Remaining 901/10000 (month/year)`
- Once you (s)ubmit a usage of 1000 units (by typing `s foo 1000`), the `Remaining 901/10000 (month/year)` become `Remaining 9901 (year)`
- The last consumption of 10_000 units fully depletes the remaining 9901 annual credits, and brings you into the overage, i.e. the included quantity of `Remaining 9901 (year)` becomes `99 consumed`

## TODO

- [ ] compensating action / compensating usage for an unsubmittable api call, it it exists. needs to make handleevent recursive
- [x] marketplace client to support batch
- [ ] collect plans separately in JSON to avoid duplication
- [ ] Floats can be negative

## Missing features

- [ ] Instrumentation, logging, metrics

## Wild ideas

- [ ] track per meter which hours have been ever submitted in a large bitfield. 
  - 365days/year * 24h/day * 1bit/(h*meter) / 8bit/byte * (4/3 extension due to base64)== 1460 byte/(year*meter). 
  - With an additional overhead of 1460 bytes per year and meter, we can track which in which hours we have submitted metering values.
