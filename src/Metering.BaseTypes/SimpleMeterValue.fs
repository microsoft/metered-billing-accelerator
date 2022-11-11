// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

type ConsumedQuantity = 
    { Amount: Quantity
      Created: MeteringDateTime 
      LastUpdate: MeteringDateTime }
    
    override this.ToString() = sprintf "%s consumed"  (this.Amount.ToString())

    member this.increaseConsumption now amount = { this with Amount = this.Amount + amount ; LastUpdate = now }

    static member create now amount = { Amount = amount; Created = now ; LastUpdate = now }

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
    
    /// Subtracts the given Quantity from a MeterValue 
    member this.subtractQuantity (now: MeteringDateTime) (quantity: Quantity) : SimpleMeterValue =
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

    static member createIncluded (now: MeteringDateTime) (quantity: Quantity) : SimpleMeterValue =
        IncludedQuantity { Quantity = quantity; Created = now; LastUpdate = now }
    
    static member someHandleQuantity (currentPosition: MeteringDateTime) (quantity: Quantity) (current: SimpleMeterValue option) : SimpleMeterValue option =
        let subtract quantity (meterValue: SimpleMeterValue) = 
            meterValue.subtractQuantity currentPosition quantity
        
        current
        |> Option.bind ((subtract quantity) >> Some) 

    static member newBillingCycle (now: MeteringDateTime) (x: SimpleConsumptionBillingDimension) : (ApplicationInternalMeterName * SimpleMeterValue) =
        (x.ApplicationInternalMeterName, IncludedQuantity { Quantity = x.IncludedQuantity; Created = now; LastUpdate = now })
    
    static member containsReportableQuantities (this: SimpleMeterValue) : bool = 
        match this with
        | ConsumedQuantity _ -> true
        | _ -> false

    member this.closeHour marketplaceResourceId (plan: Plan) (name: ApplicationInternalMeterName) : (MarketplaceRequest list * SimpleMeterValue) = 
        match this with
        | IncludedQuantity _ -> (List.empty, this)
        | ConsumedQuantity q -> 
            let dimensionId = 
                match plan.BillingDimensions.find name with 
                | SimpleConsumptionBillingDimension x -> x.DimensionId
                | _ -> failwith "bad dimension"
            
            let marketplaceRequest =
                { MarketplaceResourceId = marketplaceResourceId
                  PlanId = plan.PlanId
                  DimensionId = dimensionId
                  Quantity = q.Amount
                  EffectiveStartTime = q.LastUpdate |> MeteringDateTime.beginOfTheHour }
            ( [ marketplaceRequest], ConsumedQuantity (ConsumedQuantity.create (MeteringDateTime.now()) Quantity.Zero))

    static member createNewMeterValueDuringBillingPeriod (now: MeteringDateTime) (quantity: Quantity) : SimpleMeterValue =
        ConsumedQuantity.create now quantity
        |> ConsumedQuantity
