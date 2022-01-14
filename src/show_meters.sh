#!/bin/bash

function get_access_token {
  echo "$(curl \
      --silent \
      --request POST \
      --data-urlencode "response_type=token" \
      --data-urlencode "grant_type=client_credentials" \
      --data-urlencode "scope=https://storage.azure.com/.default" \
      --data-urlencode "client_id=${AZURE_METERING_INFRA_CLIENT_ID}" \
      --data-urlencode "client_secret=${AZURE_METERING_INFRA_CLIENT_SECRET}" \
      "https://login.microsoftonline.com/${AZURE_METERING_INFRA_TENANT_ID}/oauth2/v2.0/token" | \
          jq -r ".access_token" )"
}

if [ $# -ne 1 ]; then 
   echo "Specify partition ID to read from, for example: 

      $0 3

	    or 

      $0 1 | jq '.meters[\"fdc778a6-1281-40e4-cade-4a5fc11f5440\"].currentMeters'

		or

      $0 1 | jq -r '([\"Dimension\",\"Consumption\"] | (.,map(length*\"-\"))), (.meters[\"fdc778a6-1281-40e4-cade-4a5fc11f5440\"].currentMeters[] | [.dimensionId,.meterValue.consumed.consumedQuantity]) | @tsv'
	"

   exit 1
fi

partitionId=$1

access_token="$(get_access_token)"
url="${AZURE_METERING_INFRA_SNAPSHOTS_CONTAINER}/${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/${partitionId}/latest.json.gz"

json="$(curl --silent \
    --header "x-ms-version: 2019-12-12" \
    --header "x-ms-blob-type: BlockBlob" \
    --header "Authorization: Bearer ${access_token}" \
    "${url}" | gzip -d | jq -M '.' )"
	
echo "${json}"
