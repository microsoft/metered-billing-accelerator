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

subscriptionId="$(    get-value-or-fail '.initConfig.subscriptionId' )"
location="$(          get-value-or-fail '.initConfig.location' )"
resourceGroupName="$( get-value-or-fail '.initConfig.resourceGroupName' )"
suffix="$(            get-value-or-fail '.initConfig.suffix' )"
groupName="$(         get-value-or-fail '.initConfig.aadDesiredGroupName' )"
useAppInsights="$(    get-value-or-fail '.initConfig.useAppInsights' )"

az account set --subscription "${subscriptionId}"
account="$( az account show | jq .)"
# put-value '.deployment.subscriptionId'   "$( echo "${account}" | jq -r '.id' )"
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
    groupId="$( az ad group create \
        --display-name  "$( get-value '.initConfig.aadDesiredGroupName' )" \
        --mail-nickname "$( get-value '.initConfig.aadDesiredGroupName' )" \
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
# It could happen that the subscription doesn't have the resource provider Microsoft.Solutions registered.
# In such a case, the "Appliance Resource Provider" cannot be found
# https://github.com/MicrosoftDocs/azure-docs/issues/55581#issuecomment-638496371
#
jsonpath='.aad.applianceResourceProviderObjectID'
applianceResourceProviderObjectID="$( get-value "${jsonpath}" )"
if [ -z "${applianceResourceProviderObjectID}" ] || [ "${applianceResourceProviderObjectID}" == "null" ]
then
    az provider register --namespace "Microsoft.Solutions"
    
    # az provider show -n Microsoft.Solutions
    
    applianceResourceProviderObjectID="$( az ad sp list --display-name "Appliance Resource Provider" | jq -r .[0].id )"

    put-value "${jsonpath}" "${applianceResourceProviderObjectID}"
fi
echo "🔑 Appliance Resource Provider ID: ${applianceResourceProviderObjectID}"

msgraph="$( az ad sp show --id 00000003-0000-0000-c000-000000000000 | jq . )"
# echo "${msgraph}" | jq .
put-value '.aad.msgraph.resourceId' "$( echo "${msgraph}" | jq -r .id )"
put-value '.aad.msgraph.appRoleId'  "$( echo "${msgraph}" | jq -r '.appRoles[] | select(.value | contains("Application.ReadWrite.OwnedBy")) | .id' )"

#
# Determine the software version to be deployed within the ARM script
#

# commitId="$( git log --format='%H' -n 1 )"
webAppVersion="$( jq -r '.version' < "${basedir}/../../version.json" )"
webAppVersion="1.0.69-beta"
put-value '.deployment.webAppVersion' "${webAppVersion}"

# zipUrl="https://github.com/microsoft/metered-billing-accelerator/releases/download/${webAppVersion}/Metering.SharedResourceBroker.windows-latest.${webAppVersion}.zip"
zipUrl="https://typora.blob.core.windows.net/typoraimages/2022/11/24/15/42/publish----BGRP8HCW5VZQF0H96MMNP0XNQ0.zip"
put-value '.deployment.zipUrl' "${zipUrl}"

notificationSecret="$( openssl rand 128 | base32 --wrap=0  )"
put-value '.managedApp.notificationSecret' "${notificationSecret}"

# 
# Perform the ARM deployment
#
# The bootstrap secret (which is needed to create service principals) is directly injected into KeyVault, we don't store it locally.
# The notification secret is needed for the Azure Marketplace setup, we we store it.
deploymentResultJSON="$( az deployment group create \
    --resource-group "${resourceGroupName}" \
    --template-file "${basedir}/isv-backend.bicep" \
    --parameters \
       suffix="${suffix}" \
       bootstrapSecretValue="$( openssl rand 128 | base32 --wrap=0 )" \
       notificationSecretValue="${notificationSecret}" \
       applianceResourceProviderObjectID="${applianceResourceProviderObjectID}" \
       securityGroupForServicePrincipal="${groupId}" \
       deploymentZip="${zipUrl}" \
       useAppInsights="${useAppInsights}" \
    --output json )"

echo "ARM Deployment: $( deploymentStatus "${deploymentResultJSON}" )"

echo "${deploymentResultJSON}" | jq . > results.json

put-json-value '.names' "$(echo "${deploymentResultJSON}" | jq '.properties.outputs.resourceNames.value' )" 

# az webapp config appsettings set \
#    --name "$( get-value '.names.appService' )" \
#    --resource-group "$( get-value '.initConfig.resourceGroupName' )" \
#    --settings WEBSITE_RUN_FROM_PACKAGE="${zipUrl}"
# 
# az webapp config appsettings set \
#    --name "$( get-value '.names.appService' )" \
#    --resource-group "$( get-value '.initConfig.resourceGroupName' )" \
#    --settings WEBSITE_RUN_FROM_PACKAGE="https://typora.blob.core.windows.net/typoraimages/2022/11/24/10/00/publish----XFVSD918H1798DRNJ1D45HAH40.zip"

# Check if the metadata has been set properly, want to see
#
# {
#   "id": "/subscriptions/../resourceGroups/../providers/Microsoft.Web/sites/../config/metadata",
#   "location": "West Europe",
#   "name": "metadata",
#   "properties": {
#     "CURRENT_STACK": "dotnetcore"
#   },
#   "type": "Microsoft.Web/sites/config"
# }
# Show metadata
# az rest --method POST --url "https://management.azure.com/subscriptions/$( get-value '.initConfig.subscriptionId' )/resourceGroups/$( get-value '.initConfig.resourceGroupName' )/providers/Microsoft.Web/sites/$( get-value '.names.appService' )/config/metadata/list?api-version=2022-03-01" | jq .properties
# Show appsettings
# az rest --method POST --url "https://management.azure.com/subscriptions/$( get-value '.initConfig.subscriptionId' )/resourceGroups/$( get-value '.initConfig.resourceGroupName' )/providers/Microsoft.Web/sites/$( get-value '.names.appService' )/config/appsettings/list?api-version=2022-03-01" | jq .properties

put-value '.aad.managedIdentityPrincipalID' "$(echo "${deploymentResultJSON}" | jq -r '.properties.outputs.managedIdentityPrincipalID.value' )" 
put-value '.managedApp.notificationUrl' "https://$( get-value '.names.appService' ).azurewebsites.net/?sig=${notificationSecret}"

#
# Make the managed identity the owner of the security group
#
echo "🔑 Set principal $( get-value '.aad.managedIdentityPrincipalID' ) to be owner of group ${groupName}"
az ad group owner add \
    --group           "${groupName}" \
    --owner-object-id "$( get-value '.aad.managedIdentityPrincipalID' )" 

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
  | jq --arg x "https://meter20221119.servicebus.windows.net/meter20221119"   '.amqpEndpoint=$x' )"

echo -n "$( get-value '.managedApp.meteringConfiguration' )" >  "${basedir}/../../managed-app/meteringConfiguration.json"

# archiveNameFormat='{Namespace}/{EventHub}/p{PartitionId}--{Year}-{Month}-{Day}--{Hour}-{Minute}-{Second}'
# put-value '.eventHub.capture.archiveNameFormat' "${archiveNameFormat}"
# 
# dataDeploymentResult="$( az deployment group create \
#     --resource-group "${resourceGroupName}" \
#     --template-file "../../deploy/modules/data.bicep" \
#     --parameters \
#        appNamePrefix="${suffix}" \
#        archiveNameFormat="$( get-value '.eventHub.capture.archiveNameFormat' )" \
#        partitionCount="13" \
#     --output json )"
# 
# echo "${dataDeploymentResult}" > data-deployment-results.json
# 
# echo "Data Backend Deployment: $( echo "${dataDeploymentResult}" | jq -r .properties.provisioningState )"
# 
echo "Finished setup..."
