# Demo code

When deploying this solution, it runs standalone, fetching events out of EventHubs, aggregating them, and forwarding the aggregated results to the Azure Metering API.

However, you as developer need to integrate the metering event emission into your own application. i.e. your application must be able to submit the metering events (and the subscription management events) into EventHub. These messages are JSON messages, dropped into EventHubs, so each system which can publish new messages to EventHubs can be a client.

## .NET

For demonstration purposes, the `Metering.Runtime.dll` .NET assembly contains a little ClientSDK class, which offers c# extension methods on top of the EventHub SDK's `EventHubProducerClient`.  The sample file `src/Demos/DemoWebApp/Pages/Index.cshtml.cs` demonstrates a simple submission: 

```csharp
// Create an EventHubProducerClient, based on a couple of environment variables:
EventHubProducerClient ehClient =
    MeteringConnections.createEventHubProducerClientForClientSDK();

await ehClient.SubmitSaaSMeterAsync(
    saasSubscriptionId: "",
    applicationInternalMeterName: "obj",
    quantity: 1.3,
    cancellationToken: ct);
```

 In this sample, the dimension corresponding to the application's `obj` identifier gets a usage of 1.3 units recorded, for the customer with subscription ID `"fdc778a6-1281-40e4-cade-4a5fc11f5440"`. 

## Shell

A very simple way to drop messages into EventHubs could be directly from the shell, again for demonstration purposes. The bash script below assumes you have a few environment variables configured, pointing to your infrastructure:

In case you want to use an existing service principal to talk to EventHubs, the variables `AZURE_METERING_INFRA_CLIENT_ID`, `AZURE_METERING_INFRA_CLIENT_SECRET` and `AZURE_METERING_INFRA_TENANT_ID` contain the required clientId, secret and AAD tenant ID. You might also use a managed identity.

You must supply the `AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME` and `AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME`, so that the system knows in which EventHubs instance to drop the message.

The script then uses the [`jq`](https://cookbook.geuer-pollmann.de/command-line-utilities/jq) utility to iteratively create the JSON payload of a `UsageReported` message, which then is posted using cURL to the HTTP endpoint of Event Hub.

```shell
#!/bin/bash

function get_access_token {
  curl \
    --silent \
    --request POST \
    --data-urlencode "response_type=token" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=${AZURE_METERING_INFRA_CLIENT_ID}" \
    --data-urlencode "client_secret=${AZURE_METERING_INFRA_CLIENT_SECRET}" \
    --data-urlencode "scope=https://eventhubs.azure.net/.default" \
    "https://login.microsoftonline.com/${AZURE_METERING_INFRA_TENANT_ID}/oauth2/v2.0/token" | \
        jq -r ".access_token"
}

function createBatchUsage {
  local saas_subscription_id="$1"
  local meter_name="$2"
  local consumption="$3"

  echo "{}"                                                                                 | \
    jq --arg x "UsageReported"                       '.Body.type=($x)'                      | \
    jq --arg x "${saas_subscription_id}"             '.Body.value.resourceId=($x)'  | \
    jq --arg x "$( date -u +"%Y-%m-%dT%H:%M:%SZ" )"  '.Body.value.timestamp=($x)'           | \
    jq --arg x "${meter_name}"                       '.Body.value.meterName=($x)'           | \
    jq --arg x "${consumption}"                      '.Body.value.quantity=($x | fromjson)' | \
    jq --arg x "${saas_subscription_id}"             '.BrokerProperties.PartitionKey=($x)'  | \
    jq -c -M | iconv --from-code=ascii --to-code=utf-8
}

function createUsage {
  local saas_subscription_id="$1"
  local meter_name="$2"
  local consumption="$3"

  echo "{}"                                                                            | \
    jq --arg x "UsageReported"                       '.type=($x)'                      | \
    jq --arg x "${saas_subscription_id}"             '.value.resourceId=($x)'  | \
    jq --arg x "$( date -u +"%Y-%m-%dT%H:%M:%SZ" )"  '.value.timestamp=($x)'           | \
    jq --arg x "${meter_name}"                       '.value.meterName=($x)'           | \
    jq --arg x "${consumption}"                      '.value.quantity=($x | fromjson)' | \
    jq -c -M | iconv --from-code=ascii --to-code=utf-8
}

function submit_single_usage {
  local saas_subscription_id="$1"
  local meter_name="$2"
  local consumption="$3"
  local access_token="$4"

  data="$(createUsage "${saas_subscription_id}" "${meter_name}" "${consumption}" )"

  curl \
    --silent \
    --url "https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/messages?api-version=2014-01&timeout=60" \
    --header "Authorization: Bearer ${access_token}" \
    --header "Content-Type: application/atom+xml;type=entry;charset=utf-8" \
    --header "BrokerProperties: {\"PartitionKey\": \"${saas_subscription_id}\"}" \
    --write-out 'Submission status: %{http_code}\nDuration: %{time_total} seconds\n\n' \
    --data "${data}"    
}

if [ $# -ne 3 ]; then 
  echo "Specify the SaaS subscription ID, the meter name, and the consumption value, for example: 

      $0 \"fdc778a6-1281-40e4-cade-4a5fc11f5440\" cpu 1000.0
  "
  exit 1
fi

saas_subscription_id=$1
meter_name=$2
consumption=$3

echo "Submit ${consumption} for ${saas_subscription_id}/${meter_name}"
access_token="$(get_access_token)"
submit_single_usage "${saas_subscription_id}" "${meter_name}" "${consumption}" "${access_token}"
```

