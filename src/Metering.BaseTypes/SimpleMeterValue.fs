// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

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
