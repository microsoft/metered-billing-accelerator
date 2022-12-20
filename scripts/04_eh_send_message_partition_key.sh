#!/bin/bash

basedir="$( dirname "$( readlink -f "$0" )" )"

function eh_send_message_partition_key {
  local message="$1"
  local partition_key="$2"
  local access_token="$3"

  curl \
    --include --no-progress-meter \
    --url "https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/messages?api-version=2014-01&timeout=60" \
    --header "Authorization: Bearer ${access_token}" \
    --header "Content-Type: application/atom+xml;type=entry;charset=utf-8" \
    --header "BrokerProperties: {\"PartitionKey\": \"${partition_key}\"}" \
    --write-out 'Submission status: %{http_code}\nDuration: %{time_total} seconds\n\n' \
    --data "${message}"    
}

if [ $# -ne 2 ]; then 
  echo "Specify the JSON and partition_key: 

  "
  exit 1
fi

message=$1
partition_key=$2
access_token="$( "${basedir}/01_get_access_token_eventhubs.sh" )"

eh_send_message_partition_id "${message}" "${partition_key}" "${access_token}"
