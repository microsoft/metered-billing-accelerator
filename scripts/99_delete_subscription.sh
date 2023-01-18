#!/bin/bash

basedir="$( dirname "$( readlink -f "$0" )" )"

function create_delete_message {
  local identifier="$1"

  if [[ "${identifier}"  == /subscription* ]] ; then
      echo '{"type": "SubscriptionDeleted"}' | jq --arg x "${identifier}" '.value.resourceUri=($x)' 
  else
      echo '{"type": "SubscriptionDeleted"}' | jq --arg x "${identifier}" '.value.resourceId=($x)' 
  fi
}

if [ $# -ne 1 ]; then 
  echo "Specify the identifier of the subscription to delete: 

      $0 37d53fe9-7b82-40d7-dfe4-e28e4cdd7d18
      $0 /subscriptions/.../resourceGroups/.../providers/microsoft.solutions/applications/chgpfix20221220
      
  "
  exit 1
fi

identifier=$1
message="$( create_delete_message "${identifier}" )"
partition_key="${identifier}"

"${basedir}/04_eh_send_message_partition_key.sh" "${message}" "${partition_key}"
