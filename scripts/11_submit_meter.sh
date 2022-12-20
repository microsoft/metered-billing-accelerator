#!/bin/bash

basedir="$( dirname "$( readlink -f "$0" )" )"

function create_usage {
  local identifier="$1"
  local meter_name="$2"
  local consumption="$3"
  
  local message  
  message="$( echo "{}"                                                        \
    | jq --arg x "UsageReported"                       '.type=($x)'            \
    | jq --arg x "$( date -u "+%Y-%m-%dT%H:%M:%SZ" )"  '.value.timestamp=($x)' \
    | jq --arg x "${meter_name}"                       '.value.meterName=($x)' \
    | jq --arg x "${consumption}"                      '.value.quantity=($x | fromjson)' )"

  if [[ "${identifier}"  == /subscription* ]] ; then
      echo "${message}" | jq --arg x "${identifier}" '.value.resourceUri=($x)' 
  else
      echo "${message}" | jq --arg x "${identifier}" '.value.resourceId=($x)' 
  fi
}

if [ $# -ne 3 ]; then 
  echo "Specify the identifier, meter name, and the consumption, for example: 

      $0 37d53fe9-7b82-40d7-dfe4-e28e4cdd7d18 automation 1
      $0 37d53fe9-7b82-40d7-dfe4-e28e4cdd7d18 email 500
      $0 /subscriptions/.../resourceGroups/.../providers/microsoft.solutions/applications/chgpfix20221220 gigabytesprocessd 8.2
      
  "
  exit 1
fi

identifier=$1
meter_name=$2
consumption=$3

message="$( create_usage "${identifier}" "${meter_name}" "${consumption}" )"
partition_key="${identifier}"

"${basedir}/04_eh_send_message_partition_key.sh" "${message}" "${partition_key}"
