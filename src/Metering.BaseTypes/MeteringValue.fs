// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open Metering.BaseTypes.WaterfallTypes

type MeterValue =
    | SimpleMeterValue of SimpleMeterValue
    | WaterfallMeterValue of WaterfallMeterValue

module MeterValue =
    let containsReportableQuantities (this: MeterValue) : bool =
        match this with
        | SimpleMeterValue x -> x |> SimpleMeterLogic.containsReportableQuantities
        | WaterfallMeterValue x -> x |> WaterfallMeterLogic.containsReportableQuantities

    let getFromDimension (billingDimension: BillingDimension) : MeterValue option =
        match billingDimension with
        | SimpleBillingDimension x -> x.Meter |> Option.map SimpleMeterValue
        | WaterfallBillingDimension x -> x.Meter |> Option.map WaterfallMeterValue

    let closeHour (marketplaceResourceId: MarketplaceResourceId) (planId: PlanId) (name: ApplicationInternalMeterName) (this: BillingDimension): (MarketplaceRequest list * BillingDimension) =
        match this with
        | SimpleBillingDimension x -> x |> SimpleMeterLogic.closeHour marketplaceResourceId planId |> (fun (requests, newVal) -> (requests, SimpleBillingDimension newVal))
        | WaterfallBillingDimension x -> x |> WaterfallMeterLogic.closeHour marketplaceResourceId planId |> (fun (requests, newVal) -> (requests, WaterfallBillingDimension newVal))

    let applyConsumption (now: MeteringDateTime) (quantity: Quantity) (billingDimension: BillingDimension) : BillingDimension =
        if not quantity.isAllowedIncomingQuantity
        then billingDimension // If the incoming value is not a real (non-negative) number, don't change anything.
        else
            match billingDimension with
            | SimpleBillingDimension simpleDimension ->
                let meterValue =
                    match simpleDimension.Meter with
                    | None ->
                        simpleDimension
                        |> SimpleMeterLogic.newBillingCycle now
                        |> SimpleMeterLogic.consume now quantity
                    | Some meterValue ->
                        meterValue
                        |> SimpleMeterLogic.consume now quantity

                SimpleBillingDimension { simpleDimension with Meter = Some meterValue }

            | WaterfallBillingDimension waterfallDimension ->
                let meterValue =
                    match waterfallDimension.Meter with
                    | None ->
                        waterfallDimension
                        |> WaterfallMeterLogic.newBillingCycle now
                        |> WaterfallMeterLogic.consume waterfallDimension now quantity
                    | Some meterValue ->
                        meterValue
                        |> WaterfallMeterLogic.consume waterfallDimension now quantity

                WaterfallBillingDimension { waterfallDimension with Meter = Some meterValue}

    let accountForExpiredSubmission (dimensionId: DimensionId) (now: MeteringDateTime) (quantity: Quantity) (billingDimension: BillingDimension) : BillingDimension =
        if not quantity.isAllowedIncomingQuantity
        then billingDimension // If the incoming value is not a real (non-negative) number, don't change anything.
        else
            match billingDimension with
            | SimpleBillingDimension simpleDimension ->
                let meterValue =
                    match simpleDimension.Meter with
                    | None ->
                        simpleDimension
                        |> SimpleMeterLogic.newBillingCycle now
                        |> SimpleMeterLogic.accountExpiredSubmission dimensionId now quantity
                    | Some meterValue ->
                        meterValue
                        |> SimpleMeterLogic.accountExpiredSubmission dimensionId now quantity

                SimpleBillingDimension { simpleDimension with Meter = Some meterValue }

            | WaterfallBillingDimension waterfallDimension ->
                let meterValue =
                    match waterfallDimension.Meter with
                    | None ->
                        waterfallDimension
                        |> WaterfallMeterLogic.newBillingCycle now
                        |> WaterfallMeterLogic.accountExpiredSubmission dimensionId waterfallDimension now quantity
                    | Some meterValue ->
                        meterValue
                        |> WaterfallMeterLogic.accountExpiredSubmission dimensionId waterfallDimension now quantity

                WaterfallBillingDimension { waterfallDimension with Meter = Some meterValue}