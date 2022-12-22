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

# consumergroupName='$Default'
# curl \
#     --no-progress-meter --include \
#     --url "https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/consumergroups/${consumergroupName}/partitions/7?api-version=2014-01" \
#     --header "Authorization: Bearer ${access_token_eh}" 
#
#
# IFS=$'\r\n' GLOBIGNORE='*' command eval partition_ids="($( curl \
#     --no-progress-meter \
#     --url "https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}?api-version=2014-01" \
#     --header "Authorization: Bearer ${access_token_eh}" \
#     | xq -r '.entry.content.EventHubDescription.PartitionIds["d2p1:string"][]' ))"
# 
# for partition_id in "${partition_ids[@]}"
# do
#    curl \
#     --no-progress-meter \
#     --url "https://${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}.servicebus.windows.net/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/partition/${partition_id}?api-version=2014-01" \
#     --header "Authorization: Bearer ${access_token_eh}"
# done