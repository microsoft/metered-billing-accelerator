# Some snippets to play with the infra

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
