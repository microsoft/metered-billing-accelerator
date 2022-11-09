// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open Metering.BaseTypes.WaterfallTypes

type BillingDimension =
    | WaterfallBillingDimension of WaterfallBillingDimension
    | SimpleConsumptionBillingDimension of SimpleConsumptionBillingDimension

// https://docs.microsoft.com/en-us/azure/marketplace/azure-app-metered-billing#billing-dimensions

/// Defines a custom unit by which the ISV can emit usage events. 
/// Billing dimensions are also used to communicate to the customer about how they will be billed for using the software. 
type BillingDimensions =
    private | Value of SimpleConsumptionBillingDimension list
          
    member this.value
        with get() =
            let v (Value x) = x
            this |> v

    static member create x = (Value x)
