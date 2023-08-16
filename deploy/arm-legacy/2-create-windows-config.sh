#!/bin/bash

file=set_vars.cmd

echo "Reading the deployment results and creating a batch file, which you can run on Windows, to set environment vars"
outputs="$( cat ./deployment.json | jq .properties.outputs.environmentConfiguration.value  )"

cat > set_vars.cmd <<EOF
setx.exe AZURE_METERING_MARKETPLACE_CLIENT_ID             $( ./get-value.sh "marketplace.client_id" )
setx.exe AZURE_METERING_MARKETPLACE_CLIENT_SECRET         $( ./get-value.sh "marketplace.client_secret" )
setx.exe AZURE_METERING_MARKETPLACE_TENANT_ID             $( ./get-value.sh "marketplace.aadTenant" )

setx.exe AZURE_METERING_INFRA_CLIENT_ID                   $( ./get-value.sh "infrastructure.client_id" )
setx.exe AZURE_METERING_INFRA_CLIENT_SECRET               $( ./get-value.sh "infrastructure.client_secret" )
setx.exe AZURE_METERING_INFRA_TENANT_ID                   $( ./get-value.sh "infrastructure.aadTenant" )

setx.exe AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME      $( echo $outputs | jq -r '.eventHubNamespaceName' )
setx.exe AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME       $( echo $outputs | jq -r '.eventHub' )
setx.exe AZURE_METERING_INFRA_CHECKPOINTS_CONTAINER       $( echo $outputs | jq -r '.checkpointContainer' )
setx.exe AZURE_METERING_INFRA_SNAPSHOTS_CONTAINER         $( echo $outputs | jq -r '.snapshotsContainer' )
setx.exe AZURE_METERING_INFRA_CAPTURE_CONTAINER           $( echo $outputs | jq -r '.captureContainer' )
setx.exe AZURE_METERING_INFRA_CAPTURE_FILENAME_FORMAT     $( echo $outputs | jq -r '.captureFormat' )
EOF

unix2dos --quiet "${file}"

echo "Created ${file}"
