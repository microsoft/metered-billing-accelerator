#!/bin/bash

# If jq is not installed already, you can get it here:
# curl --request GET --location --silent --url "https://github.com/stedolan/jq/releases/download/jq-1.6/jq-linux64" --output ./jq && chmod +x ./jq && alias jq="./jq"

# Change these two variables to reflect the name you want to give your project, 
# and the location you want it to run in.

basedir="$( dirname "$( readlink -f "$0" )" )"
cd "${basedir}"

./put-value.sh "location"                                 "westeurope"
./put-value.sh "resourceGroupName"                        "meteringhack"
./put-value.sh "eventHubNameNamespaceName"                "meteringhack-standard"
./put-value.sh "storageAccountName"                       "meteringhack"
./put-value.sh "infraServicePrincipalObjectID"            "0ee98a8b-384b-4ac5-a198-bdfb6147c083"
./put-value.sh "infraServicePrincipalObjectType"          "Group"
./put-value.sh "prefix"                                   "$(date +%Y%m%d)-chgeuer"


#./put-value.sh "naming.RAND"               "$( LC_CTYPE=C tr -dc 'a-z0-9' < /dev/urandom | fold -w 4 | head -n 1 )"

#
# This descriptive "solution AAD prefix" will be used for prefixing names of application entries in AAD
#
# ./put-value.sh "resourceGroupName"         "$( ./get-value.sh "naming.solution" )"
# Ensure that storage account name has only lowercase letters and is no more than 24 characteres long
# ./put-value.sh "naming.solutionLower"      "$( ./get-value.sh "naming.solution" | tr '[:upper:]' '[:lower:]' )"
# ./put-value.sh "naming.solutionLower18"    "$( ./get-value.sh "naming.solutionLower" | cut -c1-18 )"
# ./put-value.sh "storageAccountName"        "$( ./get-value.sh "naming.solutionLower18" )$(  ./get-value.sh "naming.RAND" )"
# ./put-value.sh "landing_page.hostname"     "app-landingpage-$(                              ./get-value.sh "naming.RAND" )"
# ./put-value.sh "license_manager.hostname"  "app-licensemanager-$(                           ./get-value.sh "naming.RAND" )"
# ./put-value.sh "webhookFuncName"           "func-$(   ./get-value.sh "naming.solution" )-$( ./get-value.sh "naming.RAND" )"
# ./put-value.sh "planName"                  "plan-$(   ./get-value.sh "naming.solution" )"
# ./put-value.sh "app_configuration_name"    "appcs-$(  ./get-value.sh "naming.solution" )"
# ./put-value.sh "keyvault_name"             "kv-$(     ./get-value.sh "naming.solution" )"
# ./put-value.sh "cosmosdb_name"             "cosmos-$( ./get-value.sh "naming.solutionLower" )"
