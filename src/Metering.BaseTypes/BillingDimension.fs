// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

// https://docs.microsoft.com/en-us/azure/marketplace/azure-app-metered-billing#billing-dimensions

/// Defines a custom unit by which the ISV can emit usage events. 
/// Billing dimensions are also used to communicate to the customer about how they will be billed for using the software. 
type BillingDimensions = Map<DimensionId, Quantity>
      
module BillingDimensions =
    let create (now: MeteringDateTime) (billingDimensions: BillingDimensions) : CurrentMeterValues = 
        let toIncluded (quantity: Quantity) : MeterValue = 
            IncludedQuantity { Quantity = quantity; Created = now; LastUpdate = now }
            
        billingDimensions
        |> Map.toSeq
        |> Seq.map(fun (dimensionId, quantity) -> (dimensionId, quantity |> toIncluded))
        |> Map.ofSeq
