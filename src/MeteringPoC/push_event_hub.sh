#!/bin/bash

access_token="$(curl \
    --silent \
    --request POST \
    --data-urlencode "response_type=token" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=${AZURE_METERING_INFRA_CLIENT_ID}" \
    --data-urlencode "client_secret=${AZURE_METERING_INFRA_CLIENT_SECRET}" \
    --data-urlencode "scope=https://eventhubs.azure.net/.default" \
    "https://login.microsoftonline.com/${AZURE_METERING_INFRA_TENANT_ID}/oauth2/v2.0/token" | \
        jq -r ".access_token")"

saas_subscription_id="32119834-65f3-48c1-b366-619df2e4c400"

meterName="cpu"

consumptionUnits=1

json="$( echo "{}"                                                                        | \
        jq --arg x "UsageReported"                       '.type=($x)'                     | \
        jq --arg x "${saas_subscription_id}"             '.value.internalResourceId=($x)' | \
        jq --arg x "$( date -u +"%Y-%m-%dT%H:%M:%SZ" )"  '.value.timestamp=($x)'          | \
        jq --arg x "${meterName}"                        '.value.meterName=($x)'          | \
        jq --arg x "${consumptionUnits}"                 '.value.quantity=($x | fromjson)' | \
        jq -c -M | iconv --from-code=ascii --to-code=utf-8 )"

# "https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/partitions/${partitionId}/messages""

echo "${json}" | jq

curl --include \
    --url "https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/messages" \
    --data-urlencode "api-version=2014-01"                                       \
    --data-urlencode "timeout=60"                                                \
    --header "Authorization: Bearer ${access_token}"                             \
    --header "Content-Type: application/atom+xml;type=entry;charset=utf-8"       \
    --header "BrokerProperties: {\"PartitionKey\": \"${saas_subscription_id}\"}" \
    --data "${json}"
