#!/bin/bash

basedir="$( dirname "$( readlink -f "$0" )" )"

function eh_send_message_partition_id {
  local message="$1"
  local partition_id="$2"
  local access_token="$3"

  curl \
    --include --no-progress-meter \
    --url "https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/partitions/{$partition_id}/messages?api-version=2014-01&timeout=60" \
    --header "Authorization: Bearer ${access_token}" \
    --header "Content-Type: application/atom+xml;type=entry;charset=utf-8" \
    --write-out 'Submission status: %{http_code}\nDuration: %{time_total} seconds\n\n' \
    --data "${message}"    
}

if [ $# -ne 2 ]; then 
  echo "Specify the JSON and partition_id: 

  "
  exit 1
fi

message=$1
partition_id=$2
access_token="$( "${basedir}/01_get_access_token_eventhubs.sh" )"

eh_send_message_partition_id "${message}" "${partition_id}" "${access_token}"
