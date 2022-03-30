// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

// https://docs.microsoft.com/en-us/azure/marketplace/azure-app-metered-billing#billing-dimensions

/// Defines a custom unit by which the ISV can emit usage events. 
/// Billing dimensions are also used to communicate to the customer about how they will be billed for using the software. 
type BillingDimensions = Map<DimensionId, IncludedQuantitySpecification>
      
module BillingDimensions =
    let create (now: MeteringDateTime) (renewalInterval: RenewalInterval) (billingDimensions: BillingDimensions) : CurrentMeterValues = 
        let initialMeterValueFromDimension (bd: IncludedQuantitySpecification) : MeterValue = 
            renewalInterval
            |> function
               | Monthly -> bd.Monthly
               | Annually -> bd.Annually
            |> function
               | Some amount -> IncludedQuantity { Quantity = amount; Created = now; LastUpdate = now }
               | None -> ConsumedQuantity { Amount = Quantity.zero; Created = now; LastUpdate = now }
            
        billingDimensions
        |> Map.toSeq
        |> Seq.map(fun (dimensionId, bd) -> (dimensionId, bd |> initialMeterValueFromDimension))
        |> Map.ofSeq
