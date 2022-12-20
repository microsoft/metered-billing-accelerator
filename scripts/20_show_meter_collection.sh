#!/bin/bash

basedir="$( dirname "$( readlink -f "$0" )" )"

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

access_token_storage=$( "${basedir}/02_get_access_token_storage.sh" )

partitionId=$1

url="${AZURE_METERING_INFRA_SNAPSHOTS_CONTAINER}/${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/${partitionId}/latest.json.gz"

json="$(curl --silent \
   --url "${url}" \
   --header "x-ms-version: 2019-12-12" \
   --header "x-ms-blob-type: BlockBlob" \
   --header "Authorization: Bearer ${access_token_storage}" \
   | gzip -d | jq -M '.' )"
	
echo "${json}"
