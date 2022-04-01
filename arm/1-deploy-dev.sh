#!/bin/bash

basedir="$( dirname "$( readlink -f "$0" )" )"
cd "${basedir}"

echo "Creating new resource group $( ./get-value.sh resourceGroupName ) for the services in $( ./get-value.sh location )..."
az group create \
    --location "$( ./get-value.sh location )" \
    --name "$( ./get-value.sh "resourceGroupName" )"

az deployment group create \
	--resource-group "$( ./get-value.sh "resourceGroupName" )" \
	--template-file "dev.bicep" \
	--parameters \
       "eventHubNameNamespaceName=$(       ./get-value.sh "eventHubNameNamespaceName" )" \
       "infraServicePrincipalObjectID=$(   ./get-value.sh "infrastructure.iam.object_id" )" \
       "infraServicePrincipalObjectType=$( ./get-value.sh "infrastructure.iam.object_type" )" \
       "prefix=$(                          ./get-value.sh "prefix" )" \
	--output json > deployment.json
