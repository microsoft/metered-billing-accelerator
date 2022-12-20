#!/bin/bash

#
# Queries the Event Hub instance and determines the number of partitions.
#
# This requires `pip install yq`
#

basedir="$( dirname "$( readlink -f "$0" )" )"

access_token_eh=$( "${basedir}/01_get_access_token_eventhubs.sh" )

curl \
    --no-progress-meter \
    --url "https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}?api-version=2014-01" \
    --header "Authorization: Bearer ${access_token_eh}" \
    | xq -r '.entry.content.EventHubDescription.PartitionCount'
