// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Types

// https://docs.microsoft.com/en-us/azure/marketplace/azure-app-metered-billing#billing-dimensions

/// Defines a custom unit by which the ISV can emit usage events. 
/// Billing dimensions are also used to communicate to the customer about how they will be billed for using the software. 
type BillingDimension =
    { /// The immutable dimension identifier referenced while emitting usage events.
      DimensionId: DimensionId
          
      IncludedQuantity: IncludedQuantitySpecification }
      
module BillingDimension =
    let createIncludedQuantityForNow (now: MeteringDateTime) (billingDimensions: BillingDimension seq) : CurrentMeterValues = 
        billingDimensions
        |> Seq.map(fun bd -> (bd.DimensionId, IncludedQuantity { Monthly = bd.IncludedQuantity.Monthly; Annually = bd.IncludedQuantity.Annually; Created = now; LastUpdate = now }))
        |> Map.ofSeq
