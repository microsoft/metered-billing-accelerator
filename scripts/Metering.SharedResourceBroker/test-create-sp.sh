#!/bin/bash

# By default, the current user has no permissions on the KeyVault (and we don't want to add this to the Bicep template)
# For testing purposes, add an RBAC assignment here

basedir="$( dirname "$( readlink -f "$0" )" )"
# basedir="/mnt/c/github/chgeuer/metered-billing-accelerator/scripts/Metering.SharedResourceBroker"
CONFIG_FILE="${basedir}/config.json"

if [ ! -f "$CONFIG_FILE" ]; then
    cp config-template.json "${CONFIG_FILE}"
    echo "You need to configure deployment settings in ${CONFIG_FILE}" 
    exit 1
fi

function get-value { 
    local key="$1" ;
    local json ;
    
    json="$( cat "${CONFIG_FILE}" )" ;
    echo "${json}" | jq -r "${key}"
}

function put-value { 
    local key="$1" ;
    local variableValue="$2" ;
    local json ;
    json="$( cat "${CONFIG_FILE}" )" ;
    echo "${json}" \
       | jq --arg x "${variableValue}" "${key}=(\$x)" \
       > "${CONFIG_FILE}"
}

resourceGroupName="$( get-value      .initConfig.resourceGroupName )"
bootstrapSecretName="$( get-value    .names.secrets.bootstrapSecret )"
notificationSecretName="$( get-value .names.secrets.notificationSecret )"
vaultName="$( get-value              .names.publisherKeyVault )"
subscription="$( get-value           .initConfig.subscriptionId )"
websiteName="$(  get-value           .names.appService )"

currentUserId="$( az ad signed-in-user show | jq -r .id )"

scope="/subscriptions/${subscription}/resourceGroups/${resourceGroupName}/providers/Microsoft.KeyVault/vaults/${vaultName}"
keyVaultSecretsUserRoleId="4633458b-17de-408a-b874-0445c86b69e6"

echo "Granting the currently logged-in user permissions to read the secrets:
user ${currentUserId} 
role ${keyVaultSecretsUserRoleId} (KeyVault Secrets User)
on scope ${scope}"

az role assignment create \
  --role "${keyVaultSecretsUserRoleId}" \
  --scope "${scope}" \
  --assignee-object-id "${currentUserId}" \
  --assignee-principal-type User 

bootstrapSecret=$(az keyvault secret show \
   --name "${bootstrapSecretName}" \
   --vault-name "${vaultName}" \
   | jq -r '.value' )

managedBy="/subscriptions/$( get-value '.initConfig.subscriptionId' )/resourceGroups/managed-app-resourcegroup/providers/Microsoft.Solutions/applications/demo$( TZ=GMT date '+%Y-%m-%d--%H-%M-%S' )"
json="$( echo "{}" | jq --arg x "${managedBy}" '.managedBy=$x' )"

echo "Simulating that the managed app ${managedBy} requests service principal creation"

response="$( curl \
  --silent \
  --request POST \
  --url "https://${websiteName}.azurewebsites.net/CreateServicePrincipalInKeyVault" \
  --header "Authorization: ${bootstrapSecret}" \
  --header 'Content-Type: application/json' \
  --data "${json}" \
  | jq . )"

# The response from the service principal creation process only contains a name of the secret...
secretName="$(     echo "${response}" | jq -r '.secretName' )"
echo "🔑 Service principal credential is stored in KeyVault secret ${secretName}"

servicePrincipalSecret=$(az keyvault secret show \
   --name "${secretName}" \
   --vault-name "${vaultName}" \
   | jq -r '.value' ) 
clientId="$(     echo "${servicePrincipalSecret}" | jq -r '.ClientID' )"
clientSecret="$( echo "${servicePrincipalSecret}" | jq -r '.ClientSecret' )"
tenantID="$(     echo "${servicePrincipalSecret}" | jq -r '.TenantID' )"

echo "🔑 Created clientId=\"${clientId}\" with clientSecret=\"${clientSecret}\" in tenant \"${tenantID}\""

waitTimeForServicePrincipalToPropagate="15"
echo "Waiting for a ${waitTimeForServicePrincipalToPropagate} seconds before using the service principal"
sleep $waitTimeForServicePrincipalToPropagate

#
# Demo that we can request a token for the given service principal, and that our group is in the groups list
#
resource="https://storage.azure.com/.default"
token_response="$( curl \
    --silent \
    --request POST \
    --url "https://login.microsoftonline.com/${tenantID}/oauth2/v2.0/token" \
    --data-urlencode "response_type=token" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=${clientId}" \
    --data-urlencode "client_secret=${clientSecret}" \
    --data-urlencode "scope=${resource}" )"

echo "Fetched an access token"
echo "Token response: "
echo "${token_response}" | jq .

claims="$( echo "${token_response}" | jq '.access_token | split(".") | .[1] | @base64d | fromjson' )"

echo "${claims}" | jq .

echo "The groups should contain $( get-value .aad.groupId ): "
echo "${claims}" | jq .groups


#
# And now call the delete hook with the marketplace notification secret, i.e. we pretend that the customer deleted the managed app
#
echo "Fetching notification secret to emulate the deletion of the managed app in our backend"
notificationSecret=$( az keyvault secret show \
   --name "${notificationSecretName}" \
   --vault-name "${vaultName}" \
   | jq -r '.value' )

echo "notificationSecret: ${notificationSecret}"

echo "POST'ing to https://${websiteName}.azurewebsites.net/resource?sig=${notificationSecret}: "

status="$( curl \
  --request POST \
  --silent \
  --url "https://${websiteName}.azurewebsites.net/resource?sig=${notificationSecret}" \
  --header 'Content-Type: application/json' \
  --data "$( echo "{}" \
     | jq --arg x "DELETE"                                 '.eventType=$x' \
     | jq --arg x "Deleted"                                '.provisioningState=$x' \
     | jq --arg x "$( TZ=GMT date +'%Y-%m-%dT%H:%M:%SZ' )" '.eventTime=$x' \
     | jq --arg x "${managedBy}"                           '.applicationId=$x' \
     )" \
  --write-out '%{http_code}' )"

if [ "${status}" == "200" ]
then
   echo "✅ service principal deletion success (HTTP ${status})"
else
   echo "⛔ service principal deletion failure (HTTP ${status})"
fi
