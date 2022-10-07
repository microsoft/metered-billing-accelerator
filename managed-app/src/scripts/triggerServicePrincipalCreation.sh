#!/bin/bash

#
# Trigger service principal creation in the publisher backend.
# Returns the name of the secret in the publisher KeyVault that contains the service principal secret.
#

response="$( curl \
  --silent \
  --request POST \
  --url "${SERVICE_PRINCIPAL_CREATION_URL}/CreateServicePrincipalInKeyVault" \
  --header "Authorization: ${BOOTSTRAP_SECRET_VALUE}" \
  --header "Content-Type: application/json" \
  --data "$( echo "{}" | jq --arg x "${MANAGED_BY}" '.managedBy=$x' )" \
  | jq '.' )"

secretName="$( echo "${response}" | jq -r '.secretName' )"

echo "Secret stored in publisher KeyVault under name ${secretName}"

output="$( echo "{}" | jq --arg x "${secretName}" '.secretName=$x' )"

# https://docs.microsoft.com/en-us/azure/azure-resource-manager/templates/deployment-script-template?tabs=CLI#work-with-outputs-from-cli-script
echo "${output}" > "${AZ_SCRIPTS_OUTPUT_PATH}"
