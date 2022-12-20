# Some snippets to play with the infra

These snippets interact with the infrastructure, in 2 ways:

- Read: Inspect the state of the system, by reading the `latest.json` files from the snapshot storage
- Write: Manipulate the state, by sending messages into Event Hub.

## Inspect the state

The system keeps state in (gzip-compressed) JSON files in the "snapshot storage" container, one JSON structure per Event Hubs partition. By reading these JSON files, you can read the 'latest' state. The aggregator snapshots state in 'regular' intervals. The shell script [`show_meters.sh`](../../src/show_meters.sh) can be used to fetch the JSON file for a specific partition. 

For example, the following command fetches the latest state for partition number 5, and formatting the JSON neatly:

```shell
../../src/show_meters.sh 5 | jq '.'
```

You'll get something similar to our local test data: 

```shell
cat ../../src/Metering.Tests/data/InternalDataStructures/MeterCollection.json | jq '.'
```

### When was the state updated?

The state file technically is a serialized `MeterCollection`. This data structure contains a `lastProcessedMessage` field, which gives you data on which partition it belongs to and when it was updated:

```shell
cat ../../src/Metering.Tests/data/InternalDataStructures/MeterCollection.json | jq .lastProcessedMessage
```

gives you

```json
{
  "partitionId": "3",
  "sequenceNumber": "5",
  "partitionTimestamp": "2022-01-28T15:36:21Z"
}
```

You can see that the state file belongs to partition `3` in the Event Hubs instance, and the last message which has been applied to the state is the message with `sequenceNumber=5`, which was sent into EventHub on `January 28th, 2022, at 3:35:21 PM UTC`. 


### Inspecting the current value of a given meter for a given subscription

Let's say the subscription with `resourceId="8151a707-467c-4105-df0b-44c3fca5880d"` ended up in a partition `5`, and we would like to see the value of the meter which is application-internally called `gigabyte_egress`: 

```shell
#!/bin/bash

partitionId="5"
resourceId="8151a707-467c-4105-df0b-44c3fca5880d"
meterName="gigabyte_egress"

../../src/show_meters.sh "${partitionId}" \
   | jq --arg x "${resourceId}" '.meters[] | select(.subscription.resourceId == $x)' \
   | jq --arg x "${meterName}"  '.subscription.plan.billingDimensions[$x].meter'

# cat ../../src/Metering.Tests/data/InternalDataStructures/MeterCollection.json \
#    | jq --arg x "${resourceId}" '.meters[] | select(.subscription.resourceId == $x)' \
#    | jq --arg x "${meterName}"  '.subscription.plan.billingDimensions[$x].meter'
```

Running this on our test data (the commented out part above) gives us:

```json
{
  "total": 100000,
  "consumption": {
    "egress_tier_1": 5000
  },
  "lastUpdate": "2022-01-28T15:36:21Z"
}
```

## Sending metering values

For demonstration purposes, the [`submit-meter.sh`](submit-meter.sh) shell script can be used to submit a metering value into event hub:

```shell

resourceId="8151a707-467c-4105-df0b-44c3fca5880d"
meterName="gigabyte_egress"
consumption=19 

./submit-meter.sh "${resourceId}" "${meterName}" "${consumption}"
#
# or
#
./submit-meter.sh 8151a707-467c-4105-df0b-44c3fca5880d gigabyte_egress 19
```

## Handling bad messages

### Inspecting whether bad messages are in the system

The metered billing aggregator is an event-sourced system. As such, state can only be changed by flowing updates (commands, writes) through Event Hub, and having the business logic handle it. Obviously, in an ideal world, only 'valid' and correct messages should be sent into the system. However, particularly during development, when "unprocessable" messages hit the system, the business logic stores these unprocessable  messages in the state file as well. 

By tapping into the `.unprocessable` property of the state, you can inspect these error messages:

```shell
partitionId="5"

# Show the complete list of unprocessable messages
../../src/show_meters.sh "${partitionId}" | jq '.unprocessable' 

# Show the sequenceNumbers from all unprocessable messages
../../src/show_meters.sh "${partitionId}" | jq '[.unprocessable[] | .position.sequenceNumber]' 
```

### Removing bad messages from the state

Let's say the `../../src/show_meters.sh 5 | jq '[.unprocessable[] | .position.sequenceNumber]'` call gave you something like this:

```json
[ 
	"10", 
	"9", 
	"7",
	"3"
]
```

So we know the messages  #3, #7, #9 and #10 in partition 5 are un-processable by our implementation. After inspecting / debugging why these messages entered the system, we want to clear our state file. You can do so by either deleting individual messages from the state, or all below a certain sequence number:

#### Remove a single unprocessable message from the state file

This command deletes message #9 from the state file belonging to partition #5:

```shell
partitionId=5
./remove-bad-message.sh "${partitionId}" 9
```

#### Remove all unprocessable message up to a specific sequence number from the state file

This command deletes message #3, #7 and #9 from the state file belonging to partition #5. However, message #10 would still be left in the state file for further debugging.

```shell
partitionId=5
./remove-bad-messages-until.sh "${partitionId}" 9
```

---------


```shell
./submit-meter.sh d8b14c3c-fcfa-4bf4-d034-be8c3f3ecab5 automation 19

../../src/show_meters.sh 5 | jq '.'
../../src/show_meters.sh 9 | jq '.meters[0]'
../../src/show_meters.sh 9 | jq '.meters[0].subscription.plan.billingDimensions.automation.meter'
watch "../../src/show_meters.sh 9 | jq '.meters[0].subscription.plan.billingDimensions.automation.meter'"

# Show the sequence numbers of unprocessed messages
../../src/show_meters.sh 0 | jq '[.unprocessable[] | .position.sequenceNumber]' 
./remove-bad-messages-until.sh 0 2

partitionId="9"
./remove-bad-message.sh "${partitionId}" 32
```

### Fetch all AVRO capture files

```shell
az storage blob download-batch \
  --destination . \
  --source "${AZURE_METERING_INFRA_CAPTURE_CONTAINER}" \
  --pattern "${AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME}/${AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME}/*.avro"
```
