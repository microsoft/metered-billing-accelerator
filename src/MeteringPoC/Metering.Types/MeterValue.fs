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

