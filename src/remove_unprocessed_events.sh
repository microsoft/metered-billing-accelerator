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
    jq --arg x "${saas_subscription_id}"             '.Body.value.internalResourceId=($x)'  | \
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
    jq --arg x "${saas_subscription_id}"             '.value.internalResourceId=($x)'  | \
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

function remove_before_including {
  local partitionId="$1"
  local sequenceId="$2"
  local access_token="$3"

 data="$(
   echo "{}"                                                                            | \
    jq --arg x "RemoveUnprocessedMessages"           '.type=($x)'                     | \
    jq --arg x "${partitionId}"                      '.value.partitionId=($x)'        | \
    jq --arg x "${sequenceId}"                       '.value.beforeIncluding=($x)'    | \
    jq -c -M | iconv --from-code=ascii --to-code=utf-8 
    )"

 curl \
    --silent \
    --url "https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/messages?api-version=2014-01&timeout=60" \
    --header "Authorization: Bearer ${access_token}" \
    --header "Content-Type: application/atom+xml;type=entry;charset=utf-8" \
    --header "BrokerProperties: {\"PartitionId\": \"${partitionId}\"}" \
    --write-out 'Submission status: %{http_code}\nDuration: %{time_total} seconds\n\n' \
    --data "${data}"    
}


if [ $# -ne 2 ]; then 
  echo "Specify the partitionId and sequence number to remove, for example: 

      $0 1 2938
  "
  exit 1
fi

partitionId=$1
sequenceId=$2

echo "Removing events ${sequenceId} from partition ${partitionId}"

access_token="$(get_access_token)"
remove_before_including "${partitionId}" "${sequenceId}" "${access_token}"
