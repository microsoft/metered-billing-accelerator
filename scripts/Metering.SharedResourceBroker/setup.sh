#!/bin/bash

echo "Running az cli $(az version | jq '."azure-cli"' ), should be 2.37.0 or higher"

basedir="$( dirname "$( readlink -f "$0" )" )"

CONFIG_FILE="${basedir}/../config.json"

if [ ! -f "$CONFIG_FILE" ]; then
    cp ../config-template.json "${CONFIG_FILE}"
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

function put-json-value { 
    local key="$1" ;
    local variableValue="$2" ;
    local json ;
    json="$( cat "${CONFIG_FILE}" )" ;
    echo "${json}" \
       | jq --arg x "${variableValue}" "${key}=(\$x | fromjson)" \
       > "${CONFIG_FILE}"
}

jsonpath=".initConfig.subscriptionId"
subscriptionId="$( get-value "${jsonpath}" )"
[ "${subscriptionId}" == "" ] && { echo "Please configure ${jsonpath} in file ${CONFIG_FILE}" ; exit 1 ; }

jsonpath=".initConfig.resourceGroupName"
resourceGroupName="$( get-value  "${jsonpath}" )"
[ "${resourceGroupName}" == "" ] && { echo "Please configure ${jsonpath} in file ${CONFIG_FILE}" ; exit 1 ; }

jsonpath=".initConfig.location"
location="$( get-value  "${jsonpath}" )"
[ "${location}" == "" ] && { echo "Please configure ${jsonpath} in file ${CONFIG_FILE}" ; exit 1 ; }

jsonpath=".initConfig.suffix"
suffix="$( get-value  "${jsonpath}" )"
[ "${suffix}" == "" ] && { echo "Please configure ${jsonpath} in file ${CONFIG_FILE}" ; exit 1 ; }

jsonpath=".initConfig.groupName"
groupName="$( get-value  "${jsonpath}" )"
[ "${groupName}" == "" ] && { echo "Please configure ${jsonpath} in file ${CONFIG_FILE}" ; exit 1 ; }

# echo "subscriptionId    ${subscriptionId}"
# echo "resourceGroupName ${resourceGroupName}"
# echo "location          ${location}"
# echo "suffix            ${suffix}"
# echo "groupName         ${groupName}"

az account set --subscription "${subscriptionId}"

echo "Running in subscription $( az account show | jq -r '.id') / $( az account show | jq -r '.name'), AAD Tenant $( az account show | jq -r '.tenantId')"

#
# Create the security group in AAD. Permissions will be assigned to  the group, so all Service principals in the group will have the same permissions
#
jsonpath='.aad.groupId'
groupId="$( get-value "${jsonpath}" )"

if [ -z "${groupId}" ] || [ "${groupId}" == "null" ]
then
    groupId="$( az ad group create \
        --display-name  "$( get-value '.initConfig.groupName' )" \
        --mail-nickname "$( get-value '.initConfig.groupName' )" \
        | jq -r .id )"

    put-value "${jsonpath}" "${groupId}"
    echo "Created group $( get-value '.initConfig.groupName' ). Group ID is ${groupId}"
else
    echo "Group $( get-value  '.initConfig.groupName' ) with ID ${groupId} existed"
fi

#
# Create the resource group
#
json="$( az group create \
    --location "${location}" \
    --name "${resourceGroupName}" )"

echo "Creation of resource group \"${resourceGroupName}\" status: $( echo "${json}" | jq -r .properties.provisioningState )"

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
echo "Appliance Resource Provider ID: ${applianceResourceProviderObjectID}"

#
# Determine the software version to be deployed within the ARM script
#
commitId="$( git log --format='%H' -n 1 )"
put-value '.deployment.commitId' "${commitId}"

zipUrl="https://isvreleases.blob.core.windows.net/backendrelease/henrikwh/SharedResourceBroker/Backend/Publish/Backend-Debug-${commitId}.zip"

put-value '.deployment.zipUrl' "${zipUrl}"

msgraph="$( az ad sp show --id 00000003-0000-0000-c000-000000000000 | jq . )"
put-value '.aad.msgraph.resourceId' "$( echo "${msgraph}" | jq -r .id )"
put-value '.aad.msgraph.appRoleId'  "$( echo "${msgraph}" | jq -r '.appRoles[] | select(.value | contains("Application.ReadWrite.OwnedBy")) | .id' )"

account="$( az account show | jq .)"
put-value '.deployment.subscriptionId'   "$( echo "${account}" | jq -r '.id' )"
put-value '.deployment.subscriptionName' "$( echo "${account}" | jq -r '.name' )"
put-value '.aad.tenantId'                "$( echo "${account}" | jq -r '.tenantId' )"

# 
# Perform the ARM deployment
#
deploymentResultJSON="$( az deployment group create \
    --resource-group "${resourceGroupName}" \
    --template-file "isv-backend.bicep" \
    --parameters \
       suffix="${suffix}" \
       bootstrapSecretValue="$(    openssl rand 128 | base32 --wrap=0 )" \
       notificationSecretValue="$( openssl rand 128 | base32 --wrap=0 )" \
       applianceResourceProviderObjectID="${applianceResourceProviderObjectID}" \
       securityGroupForServicePrincipal="${groupId}" \
       deploymentZip="${zipUrl}" \
    --output json )"

echo "ARM Deployment: $( echo "${deploymentResultJSON}" | jq -r .properties.provisioningState )"

echo "${deploymentResultJSON}" > results.json

put-json-value '.names'                          "$(echo "${deploymentResultJSON}" | jq    '.properties.outputs.resourceNames.value' )" 
put-value      '.aad.managedIdentityPrincipalID' "$(echo "${deploymentResultJSON}" | jq -r '.properties.outputs.managedIdentityPrincipalID.value' )" 

managedIdentityPrincipalID="$( get-value '.aad.managedIdentityPrincipalID' )"
graphResourceId="$( get-value '.aad.msgraph.resourceId')"
appRoleId="$( get-value '.aad.msgraph.appRoleId')"

#
# Make the managed identity the owner of the security group
#
az ad group owner add \
    --group           "${groupName}" \
    --owner-object-id "${managedIdentityPrincipalID}" 

az rest \
    --method POST \
    --uri "https://graph.microsoft.com/v1.0/servicePrincipals/${managedIdentityPrincipalID}/appRoleAssignments" \
    --headers "{'Content-Type': 'application/json'}" \
    --body "$( echo "{}" \
               | jq --arg x "${managedIdentityPrincipalID}" '.principalId=$x' \
               | jq --arg x "${graphResourceId}"            '.resourceId=$x' \
               | jq --arg x "${appRoleId}"                  '.appRoleId=$x' \
             )"

echo "Finished setup..."