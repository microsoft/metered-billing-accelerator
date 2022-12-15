#!/bin/bash

partitionId="10"
resourceId="37d53fe9-7b82-40d7-dfe4-e28e4cdd7d18"
meterName="silver_orchestration"

../../src/show_meters.sh "${partitionId}" \
   | jq --arg x "${resourceId}" '.meters[] | select(.subscription.resourceId == $x)' \
   | jq --arg x "${meterName}"  '.subscription.plan.billingDimensions[$x]'

# ../../src/show_meters.sh "${partitionId}" \
#    | jq --arg x "${resourceId}" '.meters[] | select(.subscription.resourceId == $x)' \
#    | jq --arg x "${meterName}"  '.subscription.plan.billingDimensions[$x].meter.consumed'
