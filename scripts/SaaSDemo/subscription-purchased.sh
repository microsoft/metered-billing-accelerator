#!/bin/bash

resourceId="37d53fe9-7b82-40d7-dfe4-e28e4cdd7d18" # The SaaS subscription ID of the purchase
subscriptionStart="2022-12-07T11:15:29Z"
# "resourceUri": "/subscriptions/caa4fcd0-b30f-4a39-bd65-90f0e218db3a/resourceGroups/wade-saas-metering-rg-dev/providers/Microsoft.SaaS/resources/test_purchase_1",

METERING_PLAN_JSON="$( cat plan.json )"

eventHubPartitionKey="${resourceId}"

jsonMessagePayload="$( \
   echo "${METERING_PLAN_JSON}" \
    | jq --arg x "${resourceId}"        '.value.subscription.resourceId=$x' \
    | jq --arg x "${subscriptionStart}" '.value.subscription.subscriptionStart=($x)' )"

initialMessageEventHubMessage="$( \
   echo "{}" \
     | jq --arg x "${jsonMessagePayload}"   '.Body=($x | fromjson)' \
     | jq --arg x "${eventHubPartitionKey}" '.BrokerProperties.PartitionKey=$x' )"

eventhub_access_token="$( curl \
    --silent \
    --request POST \
    --url "https://login.microsoftonline.com/${AZURE_METERING_INFRA_TENANT_ID}/oauth2/v2.0/token" \
    --data-urlencode "response_type=token" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=${AZURE_METERING_INFRA_CLIENT_ID}" \
    --data-urlencode "client_secret=${AZURE_METERING_INFRA_CLIENT_SECRET}" \
    --data-urlencode "scope=https://eventhubs.azure.net/.default" \
    | jq -r '.access_token' )"

AZURE_METERING_INFRA_EVENTHUB_URL="https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}"

submissionStatusCode="$( curl \
    --silent \
    --url "${AZURE_METERING_INFRA_EVENTHUB_URL}/messages?api-version=2014-01&timeout=60" \
    --header "Authorization: Bearer ${eventhub_access_token}" \
    --header "Content-Type: application/atom+xml;type=entry;charset=utf-8" \
    --header "BrokerProperties: {\"PartitionKey\": \"${eventHubPartitionKey}\"}" \
    --write-out '%{http_code}' \
    --data "${initialMessageEventHubMessage}" )"





echo "POST ${AZURE_METERING_INFRA_EVENTHUB_URL}/messages?api-version=2014-01&timeout=60
Content-Type: application/atom+xml;type=entry;charset=utf-8
BrokerProperties: {\"PartitionKey\": \"${eventHubPartitionKey}\"}

${initialMessageEventHubMessage}"

echo "submissionStatusCode: ${submissionStatusCode}"
