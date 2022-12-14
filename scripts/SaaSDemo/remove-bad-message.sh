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

function createRemoveUnprocessedMessage {
  local partitionId="$1"
  local sequenceNumber="$2"
  
  echo "{}" \
    | jq --arg x "RemoveUnprocessedMessages" '.type=($x)' \
    | jq --arg x "${partitionId}"  '.value.partitionId=($x)' \
    | jq --arg x "${sequenceNumber}" '.value.exactly=($x | fromjson)'
}

function submit_RemoveUnprocessedMessage {
  local partitionId="$1"
  local sequenceNumber="$2"
  local access_token="$3"

  jsonPayload="$( createRemoveUnprocessedMessage "${partitionId}" "${sequenceNumber}" )"

  curl \
    --include --no-progress-meter \
    --url "https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/partitions/{$partitionId}/messages?api-version=2014-01&timeout=60" \
    --header "Authorization: Bearer ${access_token}" \
    --header "Content-Type: application/atom+xml;type=entry;charset=utf-8" \
    --write-out 'Submission status: %{http_code}\nDuration: %{time_total} seconds\n\n' \
    --data "${jsonPayload}"    
}


if [ $# -ne 2 ]; then 
  echo "Specify the partition ID, and the sequence number of the message you want to remove, for example: 

      $0 9 24
      
  "
  exit 1
fi

partitionId=$1
sequenceNumber=$2

access_token="$(get_access_token)"
submit_RemoveUnprocessedMessage "${partitionId}" "${sequenceNumber}" "${access_token}"
