namespace Metering.Types

open Metering.Types.EventHub

type MeterValue =
    | ConsumedQuantity of ConsumedQuantity
    | IncludedQuantity of IncludedQuantity

module MeterValue =
    let createIncludedMonthly now amount =  IncludedQuantity { Annually = None; Monthly = Some amount; Created = now; LastUpdate = now }
    let createIncludedAnnually now amount =  IncludedQuantity { Annually = Some amount; Monthly = None; Created = now; LastUpdate = now }
    let createIncluded now monthlyAmount annualAmount =  IncludedQuantity { Annually = Some annualAmount; Monthly = Some monthlyAmount; Created = now; LastUpdate = now }
      
    let topupMonthlyCredits (now: MeteringDateTime) (quantity: Quantity) (pri: RenewalInterval) (meterValue: MeterValue) : MeterValue =
        match meterValue with 
        | (ConsumedQuantity(_)) -> 
            match pri with
                | Monthly -> quantity |>  createIncludedMonthly now
                | Annually -> quantity |> createIncludedAnnually now
        | (IncludedQuantity(m)) -> // If there are other credits, just update the asked one
            match pri with
                | Monthly -> m |> IncludedQuantity.setMonthly now quantity |> IncludedQuantity
                | Annually -> m |> IncludedQuantity.setAnnually now quantity |> IncludedQuantity

    // # Description of possible state transitions
    //    
    //### terms
    //
    //- `IncludedQuantity` refers to quantities which are included in the monthly plan, and are not yet fully consumed. As long as there is `IncludedQuantity`, we don't report to the metering API.
    //- `ConsumedQuantity` is something the customer needs to pay for, and which needs to be sliced into these 1-hour-aggregates.
    //- The `UsageToBeReported` collection in the state is essentially a TODO-list of HTTP calls which must be made, i.e. each successful POST to the metering API later can remove the entry from the `UsageToBeReported` collection.
    //
    //### Pseudo algorithm, just representing the data structure which aggregates
    //
    //- Certain maintenance operations must happen before the event in question can be deducted from the state
    //- Maintenance
    //  - If the event's timestamp (or the current time) indicates that a new billing hour has started since the last processed events
    //    - copy all meter aggregates from the last hour into the `UsageToBeReported` collection, and
    //    - reset the meter aggregates
    //  - If the event's timestamp indicates that a new billing period (month) has started since the last processed event, reset the quantity for all meters which have included quantities
    //- Apply the incoming event to the appropriate meter, considering three cases:
    //  1. If there is remaining free quantity, and the reported usage is smaller, simply deduct it
    //  2. If there is remaining free quantity, but the reported usage is larger, set the difference as Consumed (to-be-paid) overage
    //  3. If there is overage, increment it by the reported quantity
    //
    //### Sample code for applying a single event after maintenance
    //
    //```F#
    //  | IncludedQuantity(remaining) -> 
    //      if remaining > reported
    //      then IncludedQuantity({ Quantity = remaining - reported})
    //      else ConsumedQuantity({ Quantity = reported - remaining})
    //  | ConsumedQuantity(consumed) ->
    //      ConsumedQuantity({ Quantity = consumed + reported })
    //```
    
    /// Subtracts the given Quantity from a MeterValue 
    let subtractQuantity (now: MeteringDateTime) (quantity: Quantity) (meterValue: MeterValue) : MeterValue =
        meterValue
        |> function
           | ConsumedQuantity consumedQuantity -> 
                consumedQuantity
                |> ConsumedQuantity.increaseConsumption now quantity
                |> ConsumedQuantity
           | IncludedQuantity iq ->
                match (iq.Annually, iq.Monthly) with
                | (None, None) -> 
                    quantity
                    |> ConsumedQuantity.create now 
                    |> ConsumedQuantity
                | (None, Some remainingMonthly) -> 
                    // if there's only monthly stuff, deduct from the monthly side
                    if remainingMonthly > quantity
                    then 
                        iq
                        |> IncludedQuantity.decreaseMonthly now quantity
                        |> IncludedQuantity
                    else 
                        quantity - remainingMonthly
                        |> ConsumedQuantity.create now 
                        |> ConsumedQuantity
                | (Some remainingAnnually, None) -> 
                    // if there's only annual stuff, deduct from there
                    if remainingAnnually > quantity
                    then
                        iq
                        |> IncludedQuantity.decreaseAnnually now quantity 
                        |> IncludedQuantity
                    else 
                        quantity - remainingAnnually
                        |> ConsumedQuantity.create now 
                        |> ConsumedQuantity
                | (Some remainingAnnually, Some remainingMonthly) -> 
                    // if there's both annual and monthly credits, first take from monthly, them from annual
                    if remainingMonthly > quantity
                    then 
                        iq
                        |> IncludedQuantity.decreaseMonthly now quantity
                        |> IncludedQuantity
                    else 
                        if remainingAnnually > quantity - remainingMonthly
                        then
                            iq
                            |> IncludedQuantity.removeMonthly now
                            |> IncludedQuantity.decreaseAnnually now (quantity - remainingMonthly)
                            |> IncludedQuantity
                        else 
                            quantity - remainingAnnually - remainingMonthly
                            |> ConsumedQuantity.create now
                            |> ConsumedQuantity

    let someHandleQuantity (quantity: Quantity) (currentPosition: MessagePosition) (current: MeterValue option) : MeterValue option =
        let subtract quantity meterValue = 
            meterValue |> subtractQuantity currentPosition.PartitionTimestamp quantity
        
        current
        |> Option.bind ((subtract quantity) >> Some) 

