// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open Metering.BaseTypes.WaterfallTypes

type BillingDimension =
    | WaterfallBillingDimension of WaterfallBillingDimension
    | SimpleConsumptionBillingDimension of SimpleConsumptionBillingDimension

module BillingDimension =
    /// Indicates whether a certain BillingDimension contains a given DimensionId
    let hasDimensionId (dimensionId: DimensionId) (billingDimension: BillingDimension) : bool =
        match billingDimension with 
        | WaterfallBillingDimension x -> x.Tiers |> List.exists (fun item -> item.DimensionId = dimensionId)
        | SimpleConsumptionBillingDimension x -> x.DimensionId = dimensionId

    let newBillingCycle (now: MeteringDateTime) (bd: BillingDimension) : BillingDimension =
        match bd with
        | SimpleConsumptionBillingDimension x -> 
            x
            |> SimpleMeterLogic.newBillingCycle now
            |> (fun a -> { x with Meter = Some a})
            |> SimpleConsumptionBillingDimension
        | WaterfallBillingDimension x ->
            x
            |> WaterfallMeterLogic.newBillingCycle now
            |> (fun a -> { x with Meter = Some a })
            |> WaterfallBillingDimension

// https://docs.microsoft.com/en-us/azure/marketplace/azure-app-metered-billing#billing-dimensions

/// Defines a custom unit by which the ISV can emit usage events. 
/// Billing dimensions are also used to communicate to the customer about how they will be billed for using the software. 
type BillingDimensions =
    private | Value of Map<ApplicationInternalMeterName, BillingDimension> 

    member this.value
        with get() =
            let v (Value x) = x
            this |> v

    static member create x = (Value x)

    member this.find (applicationInternalMeterName: ApplicationInternalMeterName) : BillingDimension =
        this.value
        |> Map.find applicationInternalMeterName

    member this.update (updateBillingDimension: BillingDimension -> BillingDimension) =
        /// Applies the updateValue function to each value
        let updateMapValues (updateValue: 'v -> 'v) (map: Map<'k,'v>) : Map<'k,'v> =
            map
            |> Map.toSeq
            |> Seq.map (fun (k,v) -> (k, updateValue v))
            |> Map.ofSeq

        this.value
        |> updateMapValues updateBillingDimension
        |> BillingDimensions.create