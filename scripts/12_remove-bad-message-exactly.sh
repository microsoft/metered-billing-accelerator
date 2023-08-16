#!/bin/bash

basedir="$( dirname "$( readlink -f "$0" )" )"

function create_createRemoveUnprocessedMessageExactly {
  local partitionId="$1"
  local sequenceNumber="$2"
  
  echo "{}" \
    | jq --arg x "RemoveUnprocessedMessages" '.type=($x)' \
    | jq --arg x "${partitionId}"  '.value.partitionId=($x)' \
    | jq --arg x "${sequenceNumber}" '.value.exactly=($x | fromjson)'
}

if [ $# -ne 2 ]; then 
  echo "Specify the partition ID, and the exact sequence number of the event you want to remove from the list of unprocessable items, for example: 

      $0 9 24
      
  "
  exit 1
fi

partition_id=$1
sequence_number=$2

message="$( create_createRemoveUnprocessedMessageExactly "${partition_id}" "${sequence_number}" )"
"${basedir}/eh_send_message_partition_id.sh" "${message}" "${partition_id}"
