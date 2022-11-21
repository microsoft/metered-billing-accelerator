// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

type ConsumedQuantity = 
    { CurrentHour: Quantity
      Created: MeteringDateTime 
      LastUpdate: MeteringDateTime }
    
    override this.ToString() = sprintf "%s consumed"  (this.CurrentHour.ToString())

    member this.increaseConsumption now amount = { this with CurrentHour = this.CurrentHour + amount ; LastUpdate = now }

    static member create now currentHourAmount = { CurrentHour = currentHourAmount; Created = now ; LastUpdate = now }

type IncludedQuantity = 
    { Quantity: Quantity
      Created: MeteringDateTime 
      LastUpdate: MeteringDateTime }

    override this.ToString() = sprintf "Remaining %s" (this.Quantity.ToString())

    member this.set now quantity = { this with Quantity = quantity ; LastUpdate = now }

    member this.decrease now quantity = { this with Quantity = this.Quantity - quantity ; LastUpdate = now }

type SimpleMeterValue =
    | ConsumedQuantity of ConsumedQuantity
    | IncludedQuantity of IncludedQuantity

    override this.ToString() =
        match this with
        | ConsumedQuantity cq -> cq.ToString()
        | IncludedQuantity iq -> iq.ToString()

/// The 'simple consumption' represents the most simple Azure Marketplace metering model. 
/// There is a dimension with included quantities, and once the included quantities are consumed, the overage starts counting.
type SimpleBillingDimension = 
    { DimensionId: DimensionId

      /// The dimensions configured
      IncludedQuantity: Quantity 

      Meter: SimpleMeterValue option }

module SimpleMeterLogic =    
    /// Subtracts the given Quantity from a MeterValue 
    let subtractQuantity (now: MeteringDateTime) (quantity: Quantity) (this: SimpleMeterValue) : SimpleMeterValue =
        this
        |> function
           | ConsumedQuantity consumedQuantity -> 
                consumedQuantity.increaseConsumption now quantity
                |> ConsumedQuantity
           | IncludedQuantity iq ->
                let remaining = iq.Quantity
                
                if remaining >= quantity
                then 
                    iq.decrease now quantity
                    |> IncludedQuantity
                else 
                    quantity - remaining
                    |> ConsumedQuantity.create now 
                    |> ConsumedQuantity

    let createIncluded (now: MeteringDateTime) (quantity: Quantity) : SimpleMeterValue =
        IncludedQuantity { Quantity = quantity; Created = now; LastUpdate = now }
    
    let someHandleQuantity (currentPosition: MeteringDateTime) (quantity: Quantity) (current: SimpleMeterValue option) : SimpleMeterValue option =
        let subtract quantity (meterValue: SimpleMeterValue) = 
            meterValue |> subtractQuantity currentPosition quantity
        
        current
        |> Option.bind ((subtract quantity) >> Some) 

    let newBillingCycle (now: MeteringDateTime) (x: SimpleBillingDimension) : SimpleMeterValue =
        IncludedQuantity { Quantity = x.IncludedQuantity; Created = now; LastUpdate = now }
    
    let containsReportableQuantities (this: SimpleMeterValue) : bool = 
        match this with
        | ConsumedQuantity _ -> true
        | _ -> false

    let closeHour marketplaceResourceId (planId: PlanId) (this: SimpleBillingDimension) : (MarketplaceRequest list * SimpleBillingDimension) = 
        match this.Meter with
        | None -> (List.empty, this)
        | Some meter ->
            match meter with
            | IncludedQuantity _ -> (List.empty, this)
            | ConsumedQuantity q -> 
                let marketplaceRequest =
                    { MarketplaceResourceId = marketplaceResourceId
                      PlanId = planId
                      DimensionId = this.DimensionId
                      Quantity = q.CurrentHour
                      EffectiveStartTime = q.LastUpdate |> MeteringDateTime.beginOfTheHour }

                let newMeterValue = ConsumedQuantity (ConsumedQuantity.create (MeteringDateTime.now()) Quantity.Zero)
                ( [ marketplaceRequest ], { this with Meter = Some newMeterValue })

    let createNewMeterValueDuringBillingPeriod (now: MeteringDateTime) (quantity: Quantity) : SimpleMeterValue =
        ConsumedQuantity.create now quantity
        |> ConsumedQuantity
