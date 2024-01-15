#!/bin/bash

trap "exit 1" TERM
export TOP_PID=$$

echo "Running az cli $(az version | jq '."azure-cli"' ), should be 2.37.0 or higher"

basedir="$( pwd )"
basedir="$( dirname "$( readlink -f "$0" )" )"
# basedir="/mnt/c/Users/chgeuer/Desktop/metered-billing-accelerator/scripts/Metering.SharedResourceBroker"
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

function deploymentStatus {
    local json="$1"
    local status
    status="$( echo "${json}" | jq -r '.properties.provisioningState | ascii_downcase' )"
    
    if [ "${status}" == "succeeded" ]
    then
        echo "✅ Succeeded"
    else
        echo "⛔ Failed"
    fi
}

# We need a few settings to run properly
subscriptionId="$(    get-value-or-fail '.initConfig.subscriptionId' )"
location="$(          get-value-or-fail '.initConfig.location' )"
resourceGroupName="$( get-value-or-fail '.initConfig.resourceGroupName' )"
seed="$(              get-value-or-fail '.initConfig.seed' )"
groupName="$(         get-value-or-fail '.initConfig.aadDesiredGroupName' )"
useAppInsights="$(    get-value-or-fail '.initConfig.useAppInsights' )"

az account set --subscription "${subscriptionId}"
account="$( az account show | jq .)"
put-value '.deployment.subscriptionName' "$( echo "${account}" | jq -r '.name' )"
put-value '.aad.tenantId'                "$( echo "${account}" | jq -r '.tenantId' )"

echo "Running in subscription $( az account show | jq -r '.id') / $( az account show | jq -r '.name'), AAD Tenant $( az account show | jq -r '.tenantId')"

#
# Create the security group in AAD. Permissions will be assigned to  the group, so all Service principals in the group will have the same permissions
#
jsonpath='.aad.groupId'
groupId="$( get-value "${jsonpath}" )"

if [ -z "${groupId}" ] || [ "${groupId}" == "null" ]
then
    echo "⚙️ Creating group with display name \"$( get-value '.initConfig.aadDesiredGroupName' )\" and nickname \"$( get-value '.initConfig.aadDesiredGroupNickname' )\""
    groupId="$( az ad group create \
        --display-name  "$( get-value '.initConfig.aadDesiredGroupName' )" \
        --mail-nickname "$( get-value '.initConfig.aadDesiredGroupNickname' )" \
        | jq -r .id )"

    put-value "${jsonpath}" "${groupId}"
    echo "🟩 Created group $( get-value '.initConfig.aadDesiredGroupName' ). Group ID is ${groupId}"
else
    echo "💾 Group $( get-value '.initConfig.aadDesiredGroupName' ) with ID ${groupId} existed"
fi

#
# Create the resource group
#
json="$( az group create \
    --location "${location}" \
    --name "${resourceGroupName}" )"

echo "Creation of resource group \"${resourceGroupName}\" status: $( deploymentStatus "${json}" )"

#
# If the "Appliance Resource Provider" cannot be found, one probable reason is
# that the subscription doesn't have the resource provider "Microsoft.Solutions" registered.
# You can check that via 
#       az provider show --namespace Microsoft.Solutions | jq -r '.registrationState'
# 
jsonpath='.aad.applianceResourceProviderObjectID'
applianceResourceProviderObjectID="$( get-value "${jsonpath}" )"
if [ -z "${applianceResourceProviderObjectID}" ] || [ "${applianceResourceProviderObjectID}" == "null" ]
then
    az provider register --namespace "Microsoft.Solutions"
    
    # az provider show -n Microsoft.Solutions
    
    applianceResourceProviderObjectID="$( az ad sp list --display-name "Appliance Resource Provider" | jq -r '.[0].id' )"

    put-value "${jsonpath}" "${applianceResourceProviderObjectID}"
fi
echo "🔑 Appliance Resource Provider ID: ${applianceResourceProviderObjectID}"

msgraph="$( az ad sp show --id 00000003-0000-0000-c000-000000000000 | jq . )"
# echo "${msgraph}" | jq .
put-value '.aad.msgraph.resourceId' "$( echo "${msgraph}" | jq -r '.id' )"
put-value '.aad.msgraph.appRoleId'  "$( echo "${msgraph}" | jq -r '.appRoles[] | select(.value | contains("Application.ReadWrite.OwnedBy")) | .id' )"

# zipUrl="https://typora.blob.core.windows.net/typoraimages/2022/11/24/15/42/publish----BGRP8HCW5VZQF0H96MMNP0XNQ0.zip"
zipUrl="https://github.com/microsoft/metered-billing-accelerator/releases/download/1.1.21-beta/zip-deploy-Metering.SharedResourceBroker-win-x64.zip"

#
# Github stores the actual release in AWS S3, so we need to follow the redirect, so that Azure App Service ZIP Deploy can download the zip file from the correct location
#
# zipUrl="$( curl -w "%{url_effective}\n" --head --location --silent --show-error --url "${zipUrl}" --output /dev/null )"
put-value '.deployment.zipUrl' "${zipUrl}"

notificationSecret="$( openssl rand 128 | base32 --wrap=0  )"
put-value '.managedApp.notificationSecret' "${notificationSecret}"

# 
# Perform the ARM deployment
#
# The bootstrap secret (which is needed to create service principals) is directly injected into KeyVault, we don't store it locally.
# The notification secret is needed for the Azure Marketplace setup, we we store it.
# 
deploymentResultJSON="$( az deployment group create \
    --resource-group "${resourceGroupName}" \
    --template-file "${basedir}/isv-backend.bicep" \
    --parameters \
       seed="${seed}" \
       bootstrapSecretValue="$( openssl rand 128 | base32 --wrap=0 )" \
       notificationSecretValue="${notificationSecret}" \
       applianceResourceProviderObjectID="${applianceResourceProviderObjectID}" \
       securityGroupForServicePrincipal="${groupId}" \
       deploymentZip="${zipUrl}" \
       useAppInsights="${useAppInsights}" \
    --output json )"

echo "ARM Deployment: $( deploymentStatus "${deploymentResultJSON}" )"

echo "${deploymentResultJSON}" | jq . > "${basedir}/isv-backend.deloyment-result.json"

put-json-value '.names' "$(echo "${deploymentResultJSON}" | jq '.properties.outputs.resourceNames.value' )" 

put-value '.aad.managedIdentityPrincipalID' "$(echo "${deploymentResultJSON}" | jq -r '.properties.outputs.managedIdentityPrincipalID.value' )" 
put-value '.managedApp.notificationUrl' "https://$( get-value '.names.appService' ).azurewebsites.net/?sig=${notificationSecret}"

#
# Make the managed identity the owner of the security group
#
echo "🔑 Set principal $( get-value '.aad.managedIdentityPrincipalID' ) to be owner of group ${groupName}"
az ad group owner add \
    --group           "${groupName}" \
    --owner-object-id "$( get-value '.aad.managedIdentityPrincipalID' )" 

# Allow the managed identity to create service principals.
#
# The person running this script needs to be a global admin (or similar 😬) in the AAD tenant.
#
az rest \
    --method POST \
    --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$( get-value '.aad.managedIdentityPrincipalID' )/appRoleAssignments" \
    --headers "{'Content-Type': 'application/json'}" \
    --body "$( echo "{}" \
               | jq --arg x "$( get-value '.aad.managedIdentityPrincipalID' )" '.principalId=$x' \
               | jq --arg x "$( get-value '.aad.msgraph.resourceId')"          '.resourceId=$x' \
               | jq --arg x "$( get-value '.aad.msgraph.appRoleId')"           '.appRoleId=$x' \
             )"

put-json-value '.managedApp.meteringConfiguration' "$( echo "{}" \
  | jq --arg x "https://$( get-value '.names.appService' ).azurewebsites.net" '.servicePrincipalCreationURL=$x' \
  | jq --arg x "$( get-value '.initConfig.subscriptionId' )"                  '.publisherVault.publisherSubscription=$x' \
  | jq --arg x "$( get-value '.initConfig.resourceGroupName' )"               '.publisherVault.vaultResourceGroupName=$x' \
  | jq --arg x "$( get-value '.names.publisherKeyVault' )"                    '.publisherVault.vaultName=$x' \
  | jq --arg x "$( get-value '.names.secrets.bootstrapSecret' )"              '.publisherVault.bootstrapSecretName=$x' \
  | jq --arg x "https://....servicebus.windows.net/..."                       '.amqpEndpoint=$x' )"

#
# Deploy EventHubs and the storage account
#
put-value \
   '.eventHub.capture.archiveNameFormat' \
   '{Namespace}/{EventHub}/p{PartitionId}--{Year}-{Month}-{Day}--{Hour}-{Minute}-{Second}'

dataDeploymentResult="$( az deployment group create \
    --resource-group "$( get-value '.initConfig.resourceGroupName' )" \
    --template-file "${basedir}/../../deploy/modules/data.bicep" \
    --parameters \
       appNamePrefix="$( get-value '.names.appService' )" \
       archiveNameFormat="$( get-value '.eventHub.capture.archiveNameFormat' )" \
       partitionCount="13" \
       senderObjectId="$( get-value '.aad.groupId' )" \
    --output json )"

echo "${dataDeploymentResult}" > "${basedir}/data.deployment-result.json"
echo "Data Backend Deployment: $( deploymentStatus "${dataDeploymentResult}" )"

put-value '.eventHub.capture.eventHubNamespaceName'         "$( echo "${dataDeploymentResult}" | jq -r '.properties.outputs.eventHubNamespaceName.value' )" 
put-value '.eventHub.capture.eventHubName'                  "$( echo "${dataDeploymentResult}" | jq -r '.properties.outputs.eventHubName.value' )" 
put-value '.eventHub.capture.storageAccountName'            "$( echo "${dataDeploymentResult}" | jq -r '.properties.outputs.storageName.value' )" 
put-value '.managedApp.meteringConfiguration.amqpEndpoint'  "https://$( get-value '.eventHub.capture.eventHubNamespaceName' ).servicebus.windows.net/$( get-value '.eventHub.capture.eventHubName' )"

#
# Aggregator configuration
#
put-value '.aggregator.AZURE_METERING_INFRA_CAPTURE_CONTAINER'       "$( echo "${dataDeploymentResult}" | jq -r '.properties.outputs.captureBlobEndpoint.value' )" 
put-value '.aggregator.AZURE_METERING_INFRA_CHECKPOINTS_CONTAINER'   "$( echo "${dataDeploymentResult}" | jq -r '.properties.outputs.checkpointBlobEndpoint.value' )" 
put-value '.aggregator.AZURE_METERING_INFRA_SNAPSHOTS_CONTAINER'     "$( echo "${dataDeploymentResult}" | jq -r '.properties.outputs.snapshotsBlobEndpoint.value' )" 
put-value '.aggregator.AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME'  "$( get-value '.eventHub.capture.eventHubNamespaceName' )" 
put-value '.aggregator.AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME'   "$( get-value '.eventHub.capture.eventHubName' )" 
put-value '.aggregator.AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT' "$( get-value '.eventHub.capture.archiveNameFormat' )" 
put-value '.aggregator.AZURE_METERING_INFRA_TENANT_ID'               "$( get-value '.aad.tenantId' )" 
put-value '.aggregator.AZURE_METERING_MARKETPLACE_TENANT_ID'         "$( get-value '.aad.tenantId' )" 
put-value '.aggregator.AZURE_METERING_MARKETPLACE_CLIENT_ID'         "...missing" 
put-value '.aggregator.AZURE_METERING_MARKETPLACE_CLIENT_SECRET'     "...missing" 

echo "EventHub deployed to $( get-value '.managedApp.meteringConfiguration.amqpEndpoint' )"
marketplaceConfigFile="${basedir}/../../managed-app/meteringConfiguration.json"
echo -n "$( get-value '.managedApp.meteringConfiguration' )" > "${marketplaceConfigFile}"
echo "Wrote configuration for Azure Marketplace and the managed app to ${marketplaceConfigFile}"

echo "Finished setup..."
