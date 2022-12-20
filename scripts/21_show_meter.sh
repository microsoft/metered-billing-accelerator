#!/bin/bash

#
# Retrieve a specific meter
#

basedir="$( dirname "$( readlink -f "$0" )" )"

if [ $# -ne 1 ]; then 
  echo "Retrieve the billing status for a given subscription, for example: 

      $0 deadbeef-fcfa-4bf4-d034-be8c3f3ecab5
      $0 /subscriptions/724467b5-bee4-484b-bf13-d6a5505d2b51/resourceGroups/managed-app-resourcegroup/providers/microsoft.solutions/applications/chgpfix20221220
      
  "
  exit 1
fi

identifier=$1

partition_id="$( partition_id --partition-count "$( "${basedir}/03_get_partition_count.sh" )" --partition-key "${identifier}" )"
# partition_id="7"

meter="$(
   if [[ "${identifier}"  == /subscription* ]] ; then
      "${basedir}/20_show_meter_collection.sh" "${partition_id}" \
         | jq --arg x "${identifier}" '.meters[] | select(.subscription.resourceUri == $x)'
   else
      "${basedir}/20_show_meter_collection.sh" "${partition_id}" \
         | jq --arg x "${identifier}" '.meters[] | select(.subscription.resourceId == $x)'
   fi
)"

echo "${meter}"
