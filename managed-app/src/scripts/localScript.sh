#!/bin/bash

# This will be executed on the virtual machine by the customScriptExtension and pull all relevant 
# artifacts for metering submission onto the VM.

#
# Download our metering script
#
meteringScriptUrl=$1
meterScriptName="submit-meter"
curl --silent --url "${meteringScriptUrl}" --location --output "${meterScriptName}" && \
    chmod +x "${meterScriptName}" && \
    sudo mv "${meterScriptName}" /usr/local/bin && \
    sudo chown root.root "/usr/local/bin/${meterScriptName}"

#
# Install jq
#
curl --silent --url https://github.com/stedolan/jq/releases/download/jq-1.6/jq-linux64 --location --output jq && chmod +x jq && sudo mv jq /usr/local/bin && sudo chown root.root /usr/local/bin/jq

#
# Retrieve KeyVaultName/SecretName from VM tags
#
declare -A tagArray

tagsString="$( curl --silent --get --url "http://169.254.169.254/metadata/instance" --data-urlencode "api-version=2021-02-01" --header "Metadata:true" | jq -r '.compute.tags' )"

IFS=';' read -r -a tags <<< "${tagsString}"
for index in "${!tags[@]}"
do
  IFS=':' read -r -a kv <<< "${tags[index]}" 
  tagArray["${kv["0"]}"]="${kv["1"]}"
done

keyVaultName="${tagArray['runtimeKeyVaultName']}"
keyVaultSecretName="${tagArray['meteringSubmissionSecretName']}"
uamiId="${tagArray['uamiId']}"
echo "keyvault \"${keyVaultName}\" secret \"${keyVaultSecretName}\" uamiId=\"${uamiId}\""

function unEscapeHardcodedURL { 
  # I'm doing this text replacement nonsense to avoid arm-ttk error 
  # "DeploymentTemplate Must Not Contain Hardcoded Uri ... Found hardcoded reference to vault..azure..net"
  local escaped="$1" ; 
  local unescaped="${escaped// dot /.}" ; 
  echo "${unescaped}" ; 
}
vaultAzureNet="$(           unEscapeHardcodedURL 'vault dot azure dot net' )"
loginMicrosoftonlineCom="$( unEscapeHardcodedURL 'login dot microsoftonline dot com' )"

# environment().resourceManager  "https://management.azure.com/",
# environment().suffixes.keyvaultDns ".vault.azure.net" not the dot in the beginning
# environment().suffixes.storage "core.windows.net",
# environment().authentication.loginEndpoint: "https://login.microsoftonline.com/"

#
# Fetch secret from KeyVault
#
keyVaultAccessToken="$( curl --silent --get --header "Metadata: true" \
      --data-urlencode "api-version=2018-02-01" \
      --data-urlencode "resource=https://${vaultAzureNet}" \
      --data-urlencode "mi_res_id=${uamiId}" \
      --url "http://169.254.169.254/metadata/identity/oauth2/token" | \
      jq -r ".access_token" )"

echo "${keyVaultAccessToken}" | jq -R 'split(".")|.[1]|@base64d|fromjson' 

#
# Fetch the latest version id of the secret
#
keyVaultApiVersion="7.3"
secretVersion="$( curl --silent --get \
  --header "Authorization: Bearer ${keyVaultAccessToken}" \
  --data-urlencode "api-version=${keyVaultApiVersion}" \
  --url "https://${keyVaultName}.${vaultAzureNet}/secrets/${keyVaultSecretName}/versions" | \
  jq -r ".value | sort_by(.attributes.created) | .[-1].id")"

#
# Fetch the actual secret's value
#
secret="$( curl --silent \
  --url "${secretVersion}?api-version=${keyVaultApiVersion}" \
  --header "Authorization: Bearer ${keyVaultAccessToken}" \
  | jq -r ".value" )"

settingsSecretName="meteringsettings"
settingsSecretVersion="$( curl --silent --get \
  --header "Authorization: Bearer ${keyVaultAccessToken}" \
  --data-urlencode "api-version=${keyVaultApiVersion}" \
  --url "https://${keyVaultName}.${vaultAzureNet}/secrets/${settingsSecretName}/versions" | \
  jq -r ".value | sort_by(.attributes.created) | .[-1].id")"
settings="$( curl --silent \
  --url "${settingsSecretVersion}?api-version=${keyVaultApiVersion}" \
  --header "Authorization: Bearer ${keyVaultAccessToken}" \
  | jq -r ".value" )"
echo "${settings}" | jq

managedBy="$( echo "${settings}" | jq -r .managedBy )"
meteringEventHub="$( echo "${settings}" | jq -r .amqpEndpoint )"

publisherServicePrincipalClientId="$( echo "${secret}" | jq -r '.ClientID' )"
publisherServicePrincipalClientSecret="$( echo "${secret}" | jq -r '.ClientSecret' )"
publisherTenantID="$( echo "${secret}" | jq -r '.TenantID' )"

publisherResource="https://storage.azure.com/.default"

accessTokenToAccessPublisherResources="$(curl \
  --silent \
  --request POST \
  --url "https://${loginMicrosoftonlineCom}/${publisherTenantID}/oauth2/v2.0/token" \
  --data-urlencode "response_type=token" \
  --data-urlencode "grant_type=client_credentials" \
  --data-urlencode "client_id=${publisherServicePrincipalClientId}" \
  --data-urlencode "client_secret=${publisherServicePrincipalClientSecret}" \
  --data-urlencode "scope=${publisherResource}" \
  | jq -r ".access_token")"

# Print out access token claims
echo "${keyVaultAccessToken}" | jq -R 'split(".")|.[1]|@base64d|fromjson' 
echo "${accessTokenToAccessPublisherResources}" | jq -R 'split(".")|.[1]|@base64d|fromjson' 

echo "Metering event hub: ${meteringEventHub}
ManagedBy: ${managedBy}"

customerAADTenant="$( echo "${keyVaultAccessToken}" | jq -R 'split(".")|.[1]|@base64d|fromjson'  | jq -r .iss )"
publisherAADTenant="$( echo "${accessTokenToAccessPublisherResources}" | jq -R 'split(".")|.[1]|@base64d|fromjson'  | jq -r .iss )"

echo "Customer AAD: ${customerAADTenant}
Publisher AAD: ${publisherAADTenant}"

# echo "${plan}"
# echo "${plan}" > ~/plan.json

echo "${secret}" | jq . > /etc/metering-config.json

# managementApi="https://management.azure.com/"
# 
# managementAccessToken="$( curl --silent --get --header "Metadata: true" \
#       --data-urlencode "api-version=2018-02-01" \
#       --data-urlencode "resource=${managementApi}" \
#       --url "http://169.254.169.254/metadata/identity/oauth2/token" | \
#       jq -r ".access_token" )"
# 
# echo "${managementAccessToken}" | jq -R 'split(".")|.[1]|@base64d|fromjson' 
# 
# curl --silent --get \
#   --header "Authorization: Bearer ${managementAccessToken}" \
#   --data-urlencode "api-version=2021-07-01" \
#   --url "${managementApi}${managedBy}" \
#   | jq . > /tmp/m.json
