#!/bin/bash

customerTenant="942023a6-efbe-4d97-a72d-532ef7337595"  # chgeuerfte.onmicrosoft.com / chgeuerfte.aad.geuer-pollmann.de
customerSubscriptionId="724467b5-bee4-484b-bf13-d6a5505d2b51"
publisherTenant="5f9e748d-300b-48f1-85f5-3aa96d6260cb" # geuer-pollmann.de / geuerpollmannde.onmicrosoft.com

managedResourceGroup="mrg-chgeuer202207251"
managedAppResourceGroup="managed-app-resourcegroup"
managedAppName="chgeuer202207251"

managedAppRG="/subscriptions/${customerSubscriptionId}/resourceGroups/${managedAppResourceGroup}"
managedApp="${managedAppRG}/providers/Microsoft.Solutions/applications/${managedAppName}"

echo "Please log in as an authorized user in the publisher tenant who's part of the admin group for managed applications"
az login --tenant "${publisherTenant}" --use-device-code

publisherToken="$( az account get-access-token \
     --tenant "${publisherTenant}" \
     --resource-type arm | jq -r '.accessToken' )"

echo "${publisherToken}" | jq -R 'split(".")|.[1]|@base64d|fromjson' 

echo "https://management.azure.com${managedApp}?api-version=2019-07-01"

pricipalIdtofManagedAppAssignedIdentity="$( curl --silent --request GET  \
  --url "https://management.azure.com${managedApp}?api-version=2019-07-01" \
  --header "Authorization: Bearer ${publisherToken}" \
  | jq -r '.identity.principalId' )"

access_token="$( curl --silent --request POST \
   --url "https://management.azure.com${managedApp}/listTokens?api-version=2019-07-01" \
   --header "Authorization: Bearer ${publisherToken}" \
   --header "Content-Type: application/json" \
   --data '{ "authorizationAudience": "https://vault.azure.net" }' \
   | jq -r '.value[0].access_token' )"

# "xms_mirid": "/subscriptions/724467b5-bee4-484b-bf13-d6a5505d2b51/resourcegroups/managed-app-resourcegroup/providers/Microsoft.Solutions/applications/chgeuer202207251"
echo "${access_token}" | jq -R 'split(".")|.[1]|@base64d|fromjson|.xms_mirid'

# param principalId string
# param roleId string
# param uamiId string
# param scope string
# resource managedRGPermission 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' = {
#   name: guid(roleId, uamiId, scope, 'principalId')
#   properties: {
#     principalId: principalId
#     principalType: 'ServicePrincipal'
#     delegatedManagedIdentityResourceId: uamiId
#     roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
#     scope: scope
#   }
# }

IFS='' read -r -d '' armJson <<'EOF'
{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {
    "principalId": { "type": "string" },
    "roleId": { "type": "string" },
    "uamiId": { "type": "string" },
    "scope": { "type": "string" }
  },
  "resources": [
    {
      "type": "Microsoft.Authorization/roleAssignments",
      "apiVersion": "2020-04-01-preview",
      "name": "[guid(parameters('roleId'), parameters('uamiId'), parameters('scope'), 'principalId')]",
      "properties": {
        "principalId": "[parameters('principalId')]",
        "principalType": "ServicePrincipal",
        "delegatedManagedIdentityResourceId": "[parameters('uamiId')]",
        "roleDefinitionId": "[subscriptionResourceId('Microsoft.Authorization/roleDefinitions', parameters('roleId'))]",
        "scope": "[parameters('scope')]"
      }
    }
  ]
}
EOF

echo "${armJson}" | jq .

keyVaultName="kvchgpisvfdz6srcvss"
resourceId="/subscriptions/${customerSubscriptionId}/resourceGroups/${managedResourceGroup}/providers/Microsoft.KeyVault/vaults/${keyVaultName}"
keyVaultSecretsUserRole="4633458b-17de-408a-b874-0445c86b69e6"

body="$( echo '{ "properties": { "mode": "Incremental", "template": {}, "parameters": {} } }' \
 | jq --arg x "${armJson}" '.properties.template=($x | fromjson)' \
 | jq --arg x "${pricipalIdtofManagedAppAssignedIdentity}" '.properties.parameters.principalId.value=$x' \
 | jq --arg x "${keyVaultSecretsUserRole}"                 '.properties.parameters.roleId.value=$x' \
 | jq --arg x "${managedApp}"                              '.properties.parameters.uamiId.value=$x' \
 | jq --arg x "${resourceId}"                              '.properties.parameters.scope.value=$x' )"

echo "${body}" | jq .

deploymentName="rbac-$( date -u +"%Y-%m-%d--%H-%M-%S" )"
operationResult="$( curl \
   --silent \
   --request PUT \
   --url "https://management.azure.com/subscriptions/${customerSubscriptionId}/resourceGroups/${managedResourceGroup}/providers/Microsoft.Resources/deployments/${deploymentName}?api-version=2020-10-01" \
   --header "Content-Type: application/json" \
   --header "Authorization: Bearer ${publisherToken}" \
   --data "${body}" )"

echo "${operationResult}" | jq '{
  id: .id, 
  properties: {
    provisioningState: .properties.provisioningState, 
    timestamp: .properties.timestamp, 
    correlationId: .properties.correlationId
  }
}'

# LinkedAuthorizationFailed
# The client 'christian@geuer-pollmann.de' 
# with object id 'a78648ba-0157-4003-be64-98bd2b3ec54a' 
# has permission to perform action 'Microsoft.Authorization/roleAssignments/write' 
# on scope
# '/subscriptions/724467b5-bee4-484b-bf13-d6a5505d2b51/resourcegroups/mrg-chgeuer202207251/providers/Microsoft.Authorization/roleAssignments/8e8cc2a8-4a06-50ea-aca1-9c0fc0dbbb4f'; 
# however, it does not have permission to perform action 'Microsoft.Solutions/applications/write' 
# on the linked scope(s) 
# '/subscriptions/724467b5-bee4-484b-bf13-d6a5505d2b51/resourceGroups/managed-app-resourcegroup/providers/Microsoft.Solutions/applications/chgeuer202207251' or the linked scope(s) are invalid

# Subscriptions managed by me
az account list --query "[?managedByTenants[?tenantId=='5f9e748d-300b-48f1-85f5-3aa96d6260cb']][id]" -o tsv --all
