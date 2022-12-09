#!/bin/bash

trap "exit 1" TERM
export TOP_PID=$$

echo "Running az cli $(az version | jq '."azure-cli"' ), should be 2.37.0 or higher"

basedir="$( pwd )"
basedir="$( dirname "$( readlink -f "$0" )" )"
# basedir="/mnt/c/github/chgeuer/metered-billing-accelerator/scripts/Metering.SharedResourceBroker"
echo "Working in directory ${basedir}"

CONFIG_FILE="${basedir}/config.json"
if [ ! -f "$CONFIG_FILE" ]; then
    cp "${basedir}/config-template.json" "${CONFIG_FILE}"
    echo "✏️ You need to configure deployment settings in ${CONFIG_FILE}" 
    exit 1
fi

function get-value { 
    local key="$1" ;
    local json ;
    
    json="$( cat "${CONFIG_FILE}" )" ;
    echo "${json}" | jq -r "${key}"
}

function get-value-or-fail {
   local json_path="$1";
   local value;

   value="$( get-value "${json_path}" )"

   [[ -z "${value}"  ]] \
   && { echo "✏️ Please configure ${json_path} in file ${CONFIG_FILE}" > /dev/tty ; kill -s TERM $TOP_PID; }
   echo "$value"
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

function put-json-value { 
    local key="$1" ;
    local variableValue="$2" ;
    local json ;
    json="$( cat "${CONFIG_FILE}" )" ;
    echo "${json}" \
       | jq --arg x "${variableValue}" "${key}=(\$x | fromjson)" \
       > "${CONFIG_FILE}"
}

#
# Determine the names
#
prefix="$( get-value-or-fail '.names.prefix' )"
aadName="$( get-value-or-fail '.initConfig.aadDesiredGroupName' )-${prefix}-principal"
aadTenantId="$( get-value-or-fail '.aad.tenantId' )"

#
# Create the service principal
#
appJson="$( az ad app create --display-name "${aadName}" )"

theObjectId="$( echo "${appJson}" | jq -r '.id' )"
theAppId="$( echo "${appJson}" | jq -r '.appId' )"

spCreationISVJSON="$( az ad sp create --id "${theAppId}" )"
spISVId="$( echo "${spCreationISVJSON}" | jq -r .id )"

put-value '.creds.appObjectId' "${theObjectId}"
put-value '.creds.appAppId'    "${theAppId}"
put-value '.creds.spObjectId'  "${spISVId}"

theObjectId="$( get-value '.creds.appObjectId' )"
theAppId="$(    get-value '.creds.appAppId' )"
spISVId="$(     get-value '.creds.spObjectId')"

#
# Create a client_secret
#
isvGraphToken="$( az account get-access-token \
   --tenant "${aadTenantId}" \
   --resource-type ms-graph | jq -r .accessToken )"

IFS='' read -r -d '' passwordCreationBody <<EOF
{
  "displayName": "Demo Credential",
  "startDateTime": "$( TZ=GMT date '+%Y-%m-%d' )",
  "endDateTime": "$( TZ=GMT date -d '+1 year' '+%Y-%m-%d' )"
}
EOF

passwordCreationResponseJSON="$( curl \
  --silent \
  --request POST \
  --url "https://graph.microsoft.com/v1.0/applications/${theObjectId}/addPassword" \
  --header "Content-Type: application/json" \
  --header "Authorization: Bearer ${isvGraphToken}" \
  --data "${passwordCreationBody}" )"

secret="$( echo "${passwordCreationResponseJSON}" | jq -r '.secretText' )"

#
# Store all the names
#
put-value '.creds.client_secret' "${secret}"
put-value '.aggregator.AZURE_METERING_INFRA_CLIENT_ID'                 "$( get-value '.creds.appAppId' )"
put-value '.aggregator.AZURE_METERING_INFRA_CLIENT_SECRET'             "$( get-value '.creds.client_secret' )"
put-value '.aggregator.AZURE_METERING_MARKETPLACE_CLIENT_ID'           "$( get-value '.creds.appAppId' )"
put-value '.aggregator.AZURE_METERING_MARKETPLACE_CLIENT_SECRET'       "$( get-value '.creds.client_secret' )"
put-value '.managedApp.offer.technicalConfiguration.aadTenantID'       "$( get-value '.aggregator.AZURE_METERING_MARKETPLACE_TENANT_ID' )" 
put-value '.managedApp.offer.technicalConfiguration.aadApplicationID'  "$( get-value '.aggregator.AZURE_METERING_MARKETPLACE_CLIENT_ID' )" 

storageBlobDataContributor="ba92f5b4-2d11-453d-a403-e96b0029c9fe"
# storageBlobDataOwner="b7e6dc6d-f1e8-4753-8033-0f276bb0955b"
# storageBlobDataReader="2a2b9908-6ea1-4ae2-8e65-a410df84e7d1"

az role assignment create \
  --role "${storageBlobDataContributor}" \
  --description "Grant the service principal 'Storage Blob Data Contributor' on the storage account" \
  --assignee-object-id "$( get-value '.creds.spObjectId' )" \
  --assignee-principal-type "ServicePrincipal" \
  --scope "/subscriptions/$( get-value '.initConfig.subscriptionId' )/resourceGroups/$( get-value '.initConfig.resourceGroupName' )/providers/Microsoft.Storage/storageAccounts/$( get-value '.eventHub.capture.storageAccountName' )" 

eventHubDataOwner='f526a384-b230-433a-b45c-95f59c4a2dec'

az role assignment create \
  --role "${eventHubDataOwner}" \
  --description "Grant the service principal 'EventHub Data Owner' on the Event Hub" \
  --assignee-object-id "$( get-value '.creds.spObjectId' )" \
  --assignee-principal-type "ServicePrincipal" \
  --scope "/subscriptions/$( get-value '.initConfig.subscriptionId' )/resourceGroups/$( get-value '.initConfig.resourceGroupName' )/providers/Microsoft.EventHub/namespaces/$( get-value '.eventHub.capture.eventHubNamespaceName' )/eventhubs/$( get-value '.eventHub.capture.eventHubName' )" 

cat <<-EOFBATCH | unix2dos > "${basedir}/set-development-environment.cmd"
	@echo off
	setx.exe AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME  $( get-value '.aggregator.AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME' )
	setx.exe AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME   $( get-value '.aggregator.AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME' )
	setx.exe AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT $( get-value '.aggregator.AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT' )
	setx.exe AZURE_METERING_INFRA_CAPTURE_CONTAINER       $( get-value '.aggregator.AZURE_METERING_INFRA_CAPTURE_CONTAINER' )
	setx.exe AZURE_METERING_INFRA_CHECKPOINTS_CONTAINER   $( get-value '.aggregator.AZURE_METERING_INFRA_CHECKPOINTS_CONTAINER' )
	setx.exe AZURE_METERING_INFRA_SNAPSHOTS_CONTAINER     $( get-value '.aggregator.AZURE_METERING_INFRA_SNAPSHOTS_CONTAINER' )
	
	setx.exe AZURE_METERING_INFRA_TENANT_ID               $( get-value '.aggregator.AZURE_METERING_INFRA_TENANT_ID' )
	setx.exe AZURE_METERING_INFRA_CLIENT_ID               $( get-value '.aggregator.AZURE_METERING_INFRA_CLIENT_ID' )
	setx.exe AZURE_METERING_INFRA_CLIENT_SECRET           $( get-value '.aggregator.AZURE_METERING_INFRA_CLIENT_SECRET' )
	
	setx.exe AZURE_METERING_MARKETPLACE_TENANT_ID         $( get-value '.aggregator.AZURE_METERING_MARKETPLACE_TENANT_ID' )
	setx.exe AZURE_METERING_MARKETPLACE_CLIENT_ID         $( get-value '.aggregator.AZURE_METERING_MARKETPLACE_CLIENT_ID' )
	setx.exe AZURE_METERING_MARKETPLACE_CLIENT_SECRET     $( get-value '.aggregator.AZURE_METERING_MARKETPLACE_CLIENT_SECRET' )
EOFBATCH

echo "You can run ${basedir}/set-development-environment.cmd on the Windows side of the house, in case you want to locally debug the environment."
