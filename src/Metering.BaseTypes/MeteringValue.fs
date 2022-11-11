// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open Metering.BaseTypes.WaterfallTypes

type MeterValue =
    | SimpleMeterValue of SimpleMeterValue
    | WaterfallMeterValue of WaterfallMeterValue

module MeterValue =
    let newBillingCycle (now: MeteringDateTime) (bd: BillingDimension) : (ApplicationInternalMeterName * MeterValue) =
        match bd with
        | SimpleConsumptionBillingDimension x -> 
            x
            |> SimpleMeterValue.newBillingCycle now
            |> (fun (a, b) -> (a, SimpleMeterValue b))
        | WaterfallBillingDimension x ->
            x
            |> WaterfallMeterValue.newBillingCycle now
            |> (fun (a, b) -> (a, WaterfallMeterValue b))
    
    let containsReportableQuantities (this: MeterValue) : bool =
        match this with
        | SimpleMeterValue x -> x |> SimpleMeterValue.containsReportableQuantities 
        | WaterfallMeterValue x -> x |> WaterfallMeterValue.containsReportableQuantities
    
    let closeHour (marketplaceResourceId: MarketplaceResourceId) (plan: Plan) (name: ApplicationInternalMeterName) (this: MeterValue): (MarketplaceRequest list * MeterValue) =
        match this with
        | SimpleMeterValue x ->    x.closeHour                        marketplaceResourceId plan name |> (fun (requests, newVal) -> (requests, SimpleMeterValue newVal))
        | WaterfallMeterValue x -> x |> WaterfallMeterValue.closeHour marketplaceResourceId plan name |> (fun (requests, newVal) -> (requests, WaterfallMeterValue newVal))

    let applyConsumption (now: MeteringDateTime) (quantity: Quantity) (this: MeterValue) : MeterValue =
        match this with
        | SimpleMeterValue x -> SimpleMeterValue (x.subtractQuantity now quantity)
        | WaterfallMeterValue x -> WaterfallMeterValue (x |> WaterfallMeterValue.consume now quantity)