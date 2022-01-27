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


#################################################

# multiMessageBody="$( echo "{}"                  | \
#   jq --arg x "[$(createBatchUsage "${saas_subscription_id}" cpu 1)]" '.=($x | fromjson)' | \
#   #jq --arg x "[$(createBatchUsage "${saas_subscription_id}" cpu 1)]" '.+=($x | fromjson)' | \
#   #jq --arg x "[$(createBatchUsage "${saas_subscription_id}" cpu 1)]" '.+=($x | fromjson)' | \
#   jq -c -M | iconv --from-code=ascii --to-code=utf-8 )" 
# 
# echo "${multiMessageBody}" | jq
# 
# # https://docs.microsoft.com/en-us/rest/api/eventhub/send-batch-events
# curl --include \
#     --url "https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/messages?api-version=2014-01&timeout=60" \
#     --header "Authorization: Bearer ${access_token}"                             \
#     --header "application/vnd.microsoft.servicebus.json"       \
#     --data "${multiMessageBody}"
# 
