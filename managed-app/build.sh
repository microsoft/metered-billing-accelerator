#!/bin/bash

customer_usage_attribution_cfg="customer_usage_attribution_ID.json"
partnerCenterId="$( jq -r '.id' < "${customer_usage_attribution_cfg}")"
[[ "$partnerCenterId" =~ ^pid-[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}-partnercenter$ ]] || { echo "Please set add customer usage attribution ID in ${customer_usage_attribution_cfg}"; exit 1; } 
echo "Using customer usage attribution ID (from your Partner Center): ${partnerCenterId}"

tempDir="$(mktemp -d)"

zipDir="$(pwd)/zip"
[[ -d "$zipDir" ]] || mkdir "$zipDir" 

#
# Patch in the partnerCenterdid
# Remove the _generator properties
#
bicep build --stdout src/mainTemplate.bicep | \
  jq --arg x "${partnerCenterId}" 'first(.resources[] | select(.type == "Microsoft.Resources/deployments") | select(.name == "THIS-IS-INVALID-PLEASE-RUN-buildsh")).name=$x' \
  | jq 'walk(if type == "object" then del(._generator) else . end)' \
  > "${tempDir}/mainTemplate.json"

#  | jq 'walk(if . == {} then empty else . end)' \

# cd "${tempDir}"
# Import-Module C:\github\Azure\arm-ttk\arm-ttk\arm-ttk.psd1
# Test-AzTemplate

cp    src/createUiDefinition.json "${tempDir}"
cp -a src/scripts                 "${tempDir}"

filename="$(date --utc --date='-1 hour' '+%Y-%m-%dT%H-%M-%SZ').zip"

cd "${tempDir}" && \
   zip -r "${zipDir}/${filename}" . &&  \
   cd .. &&  \
   rm -rf "${tempDir}"

echo "Created ${zipDir}/$filename"
