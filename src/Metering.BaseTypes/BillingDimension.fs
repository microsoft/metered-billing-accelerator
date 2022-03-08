// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

// https://docs.microsoft.com/en-us/azure/marketplace/azure-app-metered-billing#billing-dimensions

/// Defines a custom unit by which the ISV can emit usage events. 
/// Billing dimensions are also used to communicate to the customer about how they will be billed for using the software. 
type BillingDimensions = Map<DimensionId, IncludedQuantitySpecification>
      
module BillingDimensions =
    let createIncludedQuantityForNow (now: MeteringDateTime) (billingDimensions: BillingDimensions) : CurrentMeterValues = 
        billingDimensions
        |> Map.toSeq
        |> Seq.map(fun (dimensionId, bd) -> (dimensionId, IncludedQuantity { Monthly = bd.Monthly; Annually = bd.Annually; Created = now; LastUpdate = now }))
        |> Map.ofSeq
   