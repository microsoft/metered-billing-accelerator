#!/bin/bash

identifier="deadbeef-fcfa-4bf4-d034-be8c3f3ecab5"
identifier="/subscriptions/724467b5-bee4-484b-bf13-d6a5505d2b51/resourceGroups/managed-app-resourcegroup/providers/microsoft.solutions/applications/chgp20221219"
identifier="/subscriptions/724467b5-bee4-484b-bf13-d6a5505d2b51/resourceGroups/managed-app-resourcegroup/providers/microsoft.solutions/applications/chgpfix20221220"
meter_name="bandwidth"

# partition_id="$( partition_id -c "$( ./get_partition_count.sh )" -k "${identifier}" )"
partition_id="7"

meter="$(
   if [[ "${identifier}"  == /subscription* ]] ; then
      ../../src/show_meters.sh "${partition_id}" \
         | jq --arg x "${identifier}" '.meters[] | select(.subscription.resourceUri == $x)'
   else
      ../../src/show_meters.sh "${partition_id}" \
         | jq --arg x "${identifier}" '.meters[] | select(.subscription.resourceId == $x)'
   fi
)"

echo "${meter}" | jq '.subscription.plan' | jq --arg x "${meter_name}" '.billingDimensions[$x]'
