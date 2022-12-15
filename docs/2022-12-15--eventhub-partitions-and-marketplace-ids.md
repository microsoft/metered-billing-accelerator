# 2022-12-15 Event Hub partitions and marketplace IDs

This document describes design decisions related to Event Hub and the different identifiers in Azure Marketplace.

## Introduction

### Azure Marketplace IDs

When a customer purchases an Azure marketplace offer, such as a managed application or a SaaS offer, Azure Marketplace assigns the purchased subscription resource a unique ID, the **`resourceId`**. 

However, the purchased resource has a 2nd identifier, the **`resourceUri`**. In the case of a managed application, this is the ARM resource ID of the managed app, such as `/subscriptions/.../resourceGroups/.../providers/Microsoft.Solutions/applications/{name}`, in the case of a SaaS offer it looks like `/subscriptions/.../resourceGroups/.../providers/Microsoft.SaaS/resources/{name}`. The subscription ID, resource group name, and the `{name}` in these identifiers are always customer-specific.

> For an in-depth consideration, please check the blog article [Selecting the correct ID when submitting usage events to the Azure Marketplace Metered Billing API](https://techcommunity.microsoft.com/t5/fasttrack-for-azure/azure-marketplace-metered-billing-picking-the-correct-id-when/ba-p/3542373).

When submitting metering values to the [Marketplace Metered Billing API](https://learn.microsoft.com/en-us/azure/marketplace/marketplace-metering-service-apis), the submission must include at least one of these values, either the `resourceId` or the `resourceUri`. It doesn't matter which one is used, because the Azure backend can correlate these values. It is theoretically also possible to include both `resourceId` **and** `resourceUri` in a message. 

- In the case of a **managed application**, it certainly is most simple to get hold of the `resourceUri`, which is equal to the `managedBy` property on the managed resource group.
- In the case of a **SaaS offer**, using the `resourceId` is certainly most simple (because it is known to the ISV backend early in the process).

### Event Hub Partitions

**More than one**: When creating the Event Hub instance, I highly recommend to chose a partition count larger than 1. A partition in Event Hub roughly corresponds to a unit of compute, Event Hub scales horizontally across partitions. Having only a single partition means that if the underlying compute of that partition has a failure, it is not possible to publish additional events. 

**Uneven count for development purposes**: I prefer to choose some prime number as to the number of partitions (such as 3, 7, 11, 13), so that when I run 2 instances of the aggregator, I am sure the load is unevenly balanced...

**Linearizability**: Inside a partition, events are linearized, i.e. written one after the other. Each event gets a strictly monotonic sequence number assigned, i.e. the 1st event is #1, the 2nd is #2, and so forth. 

**Timestamping**: Event Hub assigns a timestamp to each message when it is received, which is the actual value we're considering the truth.

## The challenge for the ISV

When sending `MeteringUpdateEvent`s into Event Hub, such as a `SubscriptionPurchased` or `UsageReported` event, is is important that *all* event belonging to the same managed app deployment or SaaS purchase **end up in the same partition**. To achieve that, the ISV solution must consistently use exactly one of those two values, either the  `resourceId` **or** the `resourceUri`. 

For example, the following cURL command could be used here:

```shell
#!/bin/bash

# Both identifiers would be OK
identifier="/subscriptions/..../resourceGroups/.../providers/Microsoft.SaaS/resources/SaaS Accelerator Test Subscription"
identifier="fdc778a6-1281-40e4-cade-4a5fc11f5440"

meter_name="gigabyte_processed"

consumption=1

access_token="( curl \
    --silent \
    --request POST \
    --url "https://login.microsoftonline.com/${AZURE_METERING_INFRA_TENANT_ID}/oauth2/v2.0/token" \
    --data-urlencode "response_type=token" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=${AZURE_METERING_INFRA_CLIENT_ID}" \
    --data-urlencode "client_secret=${AZURE_METERING_INFRA_CLIENT_SECRET}" \
    --data-urlencode "scope=https://eventhubs.azure.net/.default" \
    | jq -r ".access_token" )"

json="$( echo "{}"                                                                      \
    | jq --arg x "UsageReported"                       '.type=($x)'              \
    | jq --arg x "$( date -u "+%Y-%m-%dT%H:%M:%SZ" )"  '.value.timestamp=($x)'   \
    | jq --arg x "${meter_name}"                       '.value.meterName=($x)'   \
    | jq --arg x "${consumption}"                      '.value.quantity=($x | fromjson)' )"

jsonPayload="$( 
  if [[ "${identifier}"  == /subscription* ]] ; then
      echo "${json}" | jq --arg x "${identifier}" '.value.resourceUri=($x)' 
  else
      echo "${json}" | jq --arg x "${identifier}" '.value.resourceId=($x)' 
  fi
)"      
      
curl \
    --no-progress-meter \
    --url "https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/messages?api-version=2014-01&timeout=60" \
    --header "Authorization: Bearer ${access_token}" \
    --header "Content-Type: application/atom+xml;type=entry;charset=utf-8" \
    --header "BrokerProperties: {\"PartitionKey\": \"${identifier}\"}" \
    --write-out 'Submission status: %{http_code}\nDuration: %{time_total} seconds\n\n' \
    --data "${jsonPayload}"    
}
```

You can see that our call to the [Event Hub REST API](https://learn.microsoft.com/en-us/rest/api/eventhub/send-event) sets an HTTP header for the partition key, such as 

```text
BrokerProperties: {"PartitionKey":"fdc778a6-1281-40e4-cade-4a5fc11f5440"}
```

or

```text
BrokerProperties: {"PartitionKey":"/subscriptions/..../resourceGroups/.../providers/Microsoft.SaaS/resources/..."}
```

This ensures that all messages belonging to the same customer purchase end up in the same partition.

## The challenge for the aggregator

A single aggregator instance might currently hold ownership of multiple Event Hub partitions. So the Emitter component in the image below might send usage events for multiple offers at the same time. In Commit [#78f9abf3d845f281d48051ecb6abbdc3481f7a23](https://github.com/microsoft/metered-billing-accelerator/commit/78f9abf3d845f281d48051ecb6abbdc3481f7a23), we ensure that batched messages to the Azure Marketplace API always belong to offers hosted in the same partition. This allows the emitter to send all API Responses from that batch call into the same event hub partition.

![](../images/2022-03-15--13-00-01.svg)
