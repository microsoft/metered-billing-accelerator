# Description of possible state transitions

- Each state transition is triggered by an incoming event. 
- Certain maintenance operations must happen before the event in question can be deducted from the state
- Maintenance

  - If the event's timestamp indicates that a new billing hour has started since the last processed events
    - copy all meter aggregates from the last hour into the `UsageToBeReported` collection, and
    - reset the meter aggregates
  - If the event's timestamp indicates that a new billing period (month) has started since the last processed event, reset the quantity for all meters which have included quantities
- Apply the incoming event to the appropriate meter, considering three cases:
  1. If there is remaining free quantity, and the reported usage is smaller, simply deduct it
  2. If there is remaining free quantity, but the reported usage is larger, set the difference as Consumed (to-be-paid) overage
  3. If there is overage, increment it by the reported quantity

```F#
  | RemainingQuantity(remaining) -> 
      if remaining > reported
      then RemainingQuantity({ Quantity = remaining - reported})
      else ConsumedQuantity({ Quantity = reported - remaining})
  | ConsumedQuantity(consumed) ->
      ConsumedQuantity({ Quantity = consumed + reported })
```
