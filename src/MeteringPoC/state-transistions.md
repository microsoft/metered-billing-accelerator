# Description of possible state transitions

### terms

- `IncludedQuantity` refers to quantities which are included in the monthly plan, and are not yet fully consumed. As long as there is `IncludedQuantity`, we don't report to the metering API.
- `ConsumedQuantity` is something the customer needs to pay for, and which needs to be sliced into these 1-hour-aggregates.
- The `UsageToBeReported` collection in the state is essentially a TODO-list of HTTP calls which must be made, i.e. each successful POST to the metering API later can remove the entry from the `UsageToBeReported` collection.

### Pseudo algorithm, just representing the data structure which aggregates

- Certain maintenance operations must happen before the event in question can be deducted from the state
- Maintenance
  - If the event's timestamp (or the current time) indicates that a new billing hour has started since the last processed events
    - copy all meter aggregates from the last hour into the `UsageToBeReported` collection, and
    - reset the meter aggregates
  - If the event's timestamp indicates that a new billing period (month) has started since the last processed event, reset the quantity for all meters which have included quantities
- Apply the incoming event to the appropriate meter, considering three cases:
  1. If there is remaining free quantity, and the reported usage is smaller, simply deduct it
  2. If there is remaining free quantity, but the reported usage is larger, set the difference as Consumed (to-be-paid) overage
  3. If there is overage, increment it by the reported quantity

### Sample code for applying a single event after maintenance

```F#
  | IncludedQuantity(remaining) -> 
      if remaining > reported
      then IncludedQuantity({ Quantity = remaining - reported})
      else ConsumedQuantity({ Quantity = reported - remaining})
  | ConsumedQuantity(consumed) ->
      ConsumedQuantity({ Quantity = consumed + reported })
```
