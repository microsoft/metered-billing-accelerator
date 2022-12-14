#!/bin/bash

function get_access_token {
  curl \
    --silent \
    --request POST \
    --url "https://login.microsoftonline.com/${AZURE_METERING_INFRA_TENANT_ID}/oauth2/v2.0/token" \
    --data-urlencode "response_type=token" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=${AZURE_METERING_INFRA_CLIENT_ID}" \
    --data-urlencode "client_secret=${AZURE_METERING_INFRA_CLIENT_SECRET}" \
    --data-urlencode "scope=https://eventhubs.azure.net/.default" \
    | jq -r ".access_token"
}

function createUsage {
  local identifier="$1"
  local meter_name="$2"
  local consumption="$3"
  local json
  
  json="$( echo "{}"                                                                      \
    | jq --arg x "UsageReported"                       '.type=($x)'              \
    | jq --arg x "$( date -u "+%Y-%m-%dT%H:%M:%SZ" )"  '.value.timestamp=($x)'   \
    | jq --arg x "${meter_name}"                       '.value.meterName=($x)'   \
    | jq --arg x "${consumption}"                      '.value.quantity=($x | fromjson)' )"

  if [[ "${identifier}"  == /subscription* ]] ; then
      echo "${json}" | jq --arg x "${identifier}" '.value.resourceUri=($x)' 
  else
      echo "${json}" | jq --arg x "${identifier}" '.value.resourceId=($x)' 
  fi
}

function submit_single_usage {
  local identifier="$1"
  local meter_name="$2"
  local consumption="$3"
  local access_token="$4"

  jsonPayload="$( createUsage "${identifier}" "${meter_name}" "${consumption}" )"

# --include
  curl \
    --no-progress-meter \
    --url "https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/messages?api-version=2014-01&timeout=60" \
    --header "Authorization: Bearer ${access_token}" \
    --header "Content-Type: application/atom+xml;type=entry;charset=utf-8" \
    --header "BrokerProperties: {\"PartitionKey\": \"${identifier}\"}" \
    --write-out 'Submission status: %{http_code}\nDuration: %{time_total} seconds\n\n' \
    --data "${jsonPayload}"    
}

if [ $# -ne 3 ]; then 
  echo "Specify the meter name, and the consumption value, for example: 

      $0 37d53fe9-7b82-40d7-dfe4-e28e4cdd7d18 automation 1
      $0 37d53fe9-7b82-40d7-dfe4-e28e4cdd7d18 email 500
      $0 37d53fe9-7b82-40d7-dfe4-e28e4cdd7d18 gigabytesprocessd 8.2
      
  "
  exit 1
fi

identifier=$1
meter_name=$2
consumption=$3

echo "Submit ${consumption} for ${identifier} / ${meter_name}"

access_token="$(get_access_token)"
submit_single_usage "${identifier}" "${meter_name}" "${consumption}" "${access_token}"
