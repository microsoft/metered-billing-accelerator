#!/bin/bash

basedir="$( dirname "$( readlink -f "$0" )" )"

if [ $# -ne 2 ]; then 
  echo "Retrieve the billing status for a given subscription, for example: 

      $0 deadbeef-fcfa-4bf4-d034-be8c3f3ecab5 bandwidth
      $0 /subscriptions/724467b5-bee4-484b-bf13-d6a5505d2b51/resourceGroups/managed-app-resourcegroup/providers/microsoft.solutions/applications/chgpfix20221220 messages
      
  "
  exit 1
fi

identifier=$1
meter_name=$2

"${basedir}/21_show_meter.sh" "${identifier}" \
   | jq --arg x "${meter_name}" '.subscription.plan.billingDimensions[$x]'
