#!/bin/bash

echo "Submitting the initial metering message to create a subscription"

#
# Fetch secret from KeyVault
#
keyVaultAccessToken="$( curl \
  --silent \
  --get \
  --url "http://169.254.169.254/metadata/identity/oauth2/token" \
  --header "Metadata: true" \
  --data-urlencode "api-version=2018-02-01" \
  --data-urlencode "resource=https://vault.azure.net" \
  --data-urlencode "mi_res_id=${RUNTIME_IDENTITY}" \
  | jq -r '.access_token' )"

#
# Fetch the latest version id of the secret
#
keyVaultApiVersion="7.3"
secretVersion="$( curl --silent --get \
  --url "https://${RUNTIME_KEYVAULT_NAME}.vault.azure.net/secrets/${METERING_SUBMISSION_SECRET_NAME}/versions" \
  --header "Authorization: Bearer ${keyVaultAccessToken}" \
  --data-urlencode "api-version=${keyVaultApiVersion}" \
  | jq -r '.value | sort_by(.attributes.created) | .[-1].id' )"

#
# Fetch the actual secret's value
#
secret="$( curl --silent \
  --url "${secretVersion}?api-version=${keyVaultApiVersion}" \
  --header "Authorization: Bearer ${keyVaultAccessToken}" \
  | jq -r '.value' )"

managedBy="$( echo "${secret}" | jq -r '.managedBy' )"
eventHubPartitionKey="${managedBy}"

currentDate="$( date -u +"%Y-%m-%dT%H:%M:%SZ" )"

jsonMessagePayload="$( \
   echo "${METERING_PLAN_JSON}" \
    | jq --arg x "${managedBy}"   '.value.subscription.resourceUri=$x' \
    | jq --arg x "${currentDate}" '.value.subscription.subscriptionStart=($x)' \
    | jq -c -M '.' \
    )"

isvClientId="$(     echo "${secret}" | jq -r '.servicePrincipalInformation.ClientID' )"
isvClientSecret="$( echo "${secret}" | jq -r '.servicePrincipalInformation.ClientSecret' )"
isvTenantId="$(     echo "${secret}" | jq -r '.servicePrincipalInformation.TenantID' )"
isvEventHubUrl="$(  echo "${secret}" | jq -r '.servicePrincipalInformation.meteringEventHub' )"

eventhub_access_token="$( curl \
    --silent \
    --request POST \
    --url "https://login.microsoftonline.com/${isvTenantId}/oauth2/v2.0/token" \
    --data-urlencode "response_type=token" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=${isvClientId}" \
    --data-urlencode "client_secret=${isvClientSecret}" \
    --data-urlencode "scope=https://eventhubs.azure.net/.default" \
    | jq -r '.access_token' )"

submissionStatusCode="$( curl \
    --silent \
    --url "${isvEventHubUrl}/messages?api-version=2014-01&timeout=60" \
    --header "Authorization: Bearer ${eventhub_access_token}" \
    --header "Content-Type: application/atom+xml;type=entry;charset=utf-8" \
    --header "BrokerProperties: {\"PartitionKey\": \"${eventHubPartitionKey}\"}" \
    --write-out '%{http_code}' \
    --data "${jsonMessagePayload}" )"

echo "POST ${isvEventHubUrl}/messages?api-version=2014-01&timeout=60
Content-Type: application/atom+xml;type=entry;charset=utf-8
BrokerProperties: {\"PartitionKey\": \"${eventHubPartitionKey}\"}

${jsonMessagePayload}"

echo "submissionStatusCode: ${submissionStatusCode}"

echo "initialMessageEventHubMessage: ${jsonMessagePayload}"

output="$( echo "{}" \
    | jq --arg x "${submissionStatusCode}" '.submissionStatusCode=$x' \
    | jq --arg x "${jsonMessagePayload}"   '.jsonMessagePayload=$x' )"

# https://docs.microsoft.com/en-us/azure/azure-resource-manager/templates/deployment-script-template?tabs=CLI#work-with-outputs-from-cli-script
echo "${output}" > "${AZ_SCRIPTS_OUTPUT_PATH}"
