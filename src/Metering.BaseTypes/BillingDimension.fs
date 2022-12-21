// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open Metering.BaseTypes.WaterfallTypes

type BillingDimension =
    | WaterfallBillingDimension of WaterfallBillingDimension
    | SimpleBillingDimension of SimpleBillingDimension

module BillingDimension =
    /// Indicates whether a certain BillingDimension contains a given DimensionId
    let hasDimensionId (dimensionId: DimensionId) (billingDimension: BillingDimension) : bool =
        match billingDimension with
        | WaterfallBillingDimension x -> x.Tiers |> List.exists (fun item -> item.DimensionId = dimensionId)
        | SimpleBillingDimension x -> x.DimensionId = dimensionId

    let newBillingCycle (now: MeteringDateTime) (bd: BillingDimension) : BillingDimension =
        match bd with
        | SimpleBillingDimension x ->
            x
            |> SimpleMeterLogic.newBillingCycle now
            |> (fun a -> { x with Meter = Some a})
            |> SimpleBillingDimension
        | WaterfallBillingDimension x ->
            x
            |> WaterfallMeterLogic.newBillingCycle now
            |> (fun a -> { x with Meter = Some a })
            |> WaterfallBillingDimension

// https://docs.microsoft.com/en-us/azure/marketplace/azure-app-metered-billing#billing-dimensions

/// Defines a custom unit by which the ISV can emit usage events.
/// Billing dimensions are also used to communicate to the customer about how they will be billed for using the software.
type BillingDimensions =
    Map<ApplicationInternalMeterName, BillingDimension>

module BillingDimensions =
    /// Applies the updateValue function to each value
    let update (f: 'v -> 'v) (map: Map<'k,'v>) : Map<'k,'v> =
        map
        |> Map.toSeq
        |> Seq.map (fun (k, v) -> (k, f v))
        |> Map.ofSeq
