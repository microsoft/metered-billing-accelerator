// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open Metering.BaseTypes.WaterfallTypes

type BillingDimension =
    | WaterfallBillingDimension of WaterfallBillingDimension
    | SimpleConsumptionBillingDimension of SimpleConsumptionBillingDimension

module BillingDimension =
    let applicationInternalMeterName (billingDimension: BillingDimension) : ApplicationInternalMeterName =
        match billingDimension with 
        | WaterfallBillingDimension x -> x.ApplicationInternalMeterName
        | SimpleConsumptionBillingDimension x -> x.ApplicationInternalMeterName

    /// Indicates whether a certain BillingDimension contains a given DimensionId
    let hasDimensionId (dimensionId: DimensionId) (billingDimension: BillingDimension) : bool =
        match billingDimension with 
        | WaterfallBillingDimension x -> x.Tiers |> List.exists (fun item -> item.DimensionId = dimensionId)
        | SimpleConsumptionBillingDimension x -> x.DimensionId = dimensionId

// https://docs.microsoft.com/en-us/azure/marketplace/azure-app-metered-billing#billing-dimensions

/// Defines a custom unit by which the ISV can emit usage events. 
/// Billing dimensions are also used to communicate to the customer about how they will be billed for using the software. 
type BillingDimensions =
    private | Value of BillingDimension list
          
    member this.value
        with get() =
            let v (Value x) = x
            this |> v

    static member create x = (Value x)

    member this.find (applicationInternalMeterName: ApplicationInternalMeterName) : BillingDimension =
        this.value
        |> List.find (fun billingDimension -> (BillingDimension.applicationInternalMeterName billingDimension) = applicationInternalMeterName)