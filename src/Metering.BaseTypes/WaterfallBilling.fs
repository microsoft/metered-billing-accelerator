// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes.WaterfallTypes

open Metering.BaseTypes

type WaterfallModelRange =
    { DimensionId: DimensionId
      LowerIncluding: Quantity
      UpperExcluding: Quantity }

type WaterfallOverageOverage =
    { DimensionId: DimensionId
      LowerIncluding: Quantity }

/// This is the 'compiled model'
type WaterfallModelRow =
    | FreeIncluded of Quantity // For included quantities, no reporting to the metering API
    | Range of WaterfallModelRange
    | Overage of WaterfallOverageOverage

type WaterfallExpandedModel =
    WaterfallModelRow list

type WaterfallMeterValue =
  { Total: Quantity
    Consumption: Map<DimensionId, Quantity>
    LastUpdate: MeteringDateTime }

/// These must be reported
type ConsumptionReport =
  { DimensionId: DimensionId
    Quantity: Quantity }

type SubtractionAggregation =
  { CurrentTotal: Quantity
    AmountToBeDeducted: Quantity
    Consumption: Map<DimensionId, Quantity> }

/// Serialization
type WaterfallBillingDimensionItem =
    { Threshold: Quantity
      DimensionId: DimensionId }

type WaterfallIncrementalDescription =
    WaterfallBillingDimensionItem list

type WaterfallBillingDimension =
    { Tiers: WaterfallIncrementalDescription

      Model: WaterfallExpandedModel option

      Meter: WaterfallMeterValue option }

module WaterfallMeterLogic =
  let expandToFullModel (waterfallTiers: WaterfallIncrementalDescription) : WaterfallExpandedModel =
    let len = waterfallTiers |> List.length

    let expanded =
      [0 .. len - 1]
      |> List.map (fun i ->
        { Threshold =
            waterfallTiers
            |> List.take (i + 1)
            |> List.sumBy _.Threshold
          DimensionId =
            waterfallTiers
            |> List.skip(i)
            |> List.head
            |> _.DimensionId })

    let included =
      expanded
      |> List.head
      |> _.Threshold
      |> FreeIncluded

    let ranges =
      expanded |> List.skip 1
      |> List.zip (expanded |> List.take (len - 1))
      |> List.map (fun (l, r) -> { DimensionId = l.DimensionId; LowerIncluding = l.Threshold; UpperExcluding = r.Threshold })
      |> List.map Range

    let overage =
      expanded
      |> List.last
      |> fun i -> { DimensionId = i.DimensionId; LowerIncluding = i.Threshold }
      |> Overage

    match included with
    | FreeIncluded x when x > Quantity.Zero -> included :: (ranges @ [overage])
    | _ -> (ranges @ [overage])

  let display : (WaterfallModelRow -> DimensionId * string) = function
    | FreeIncluded x -> (DimensionId.create "Free", $"[0 <= x <= {x}]")
    | Range x -> (x.DimensionId, $"[{x.LowerIncluding} <= x < {x.UpperExcluding})")
    | Overage x -> (x.DimensionId, $"[{x.LowerIncluding} <= x < Infinity)")

    /// Identify the ranges into which the amount might fit.
  let findRange (amount: Quantity) (model: WaterfallExpandedModel) : WaterfallModelRow list =
    /// Determine if the current total matches the given row.
    let isNotInRow (currentTotal: Quantity) (row: WaterfallModelRow) : bool =
        match row with
        | FreeIncluded x -> currentTotal < x
        | Range { LowerIncluding = lower; UpperExcluding = upper } -> lower <= currentTotal && currentTotal < upper
        | Overage { LowerIncluding = lower } -> lower <= currentTotal
        |> not

    model
    |> List.skipWhile (isNotInRow amount)

  let add (v: Quantity) : (Quantity option -> Quantity option) = function
        | None -> Some v
        | Some e -> Some (v + e)

  let subtract (agg: SubtractionAggregation) (row: WaterfallModelRow) : SubtractionAggregation =
    let augment (ct: Quantity) (a: Quantity) (c: ConsumptionReport option) (agg: SubtractionAggregation) : SubtractionAggregation=
      match c with
      | Some c when c.Quantity > Quantity.Zero -> { CurrentTotal = ct; AmountToBeDeducted = a; Consumption = agg.Consumption |> Map.change c.DimensionId (add c.Quantity) }
      | _ -> { CurrentTotal = ct; AmountToBeDeducted = a; Consumption = agg.Consumption } // Do not add empty consumption records

    let newTotal = agg.CurrentTotal + agg.AmountToBeDeducted
    match row with
    | FreeIncluded x when newTotal < x -> agg |> augment newTotal Quantity.Zero None
    | FreeIncluded x -> agg |> augment x (newTotal - x) None
    | Range { UpperExcluding = upper; DimensionId = did } when newTotal < upper -> agg |> augment newTotal Quantity.Zero (Some { DimensionId = did; Quantity = agg.AmountToBeDeducted})
    | Range { UpperExcluding = upper; DimensionId = did } -> agg |> augment upper (newTotal - upper) (Some { DimensionId = did; Quantity = upper - agg.CurrentTotal})
    | Overage { DimensionId = dim } -> agg |> augment newTotal Quantity.Zero (Some { DimensionId = dim; Quantity = agg.AmountToBeDeducted })

  let createMeterFromDimension (now: MeteringDateTime) (dimension: WaterfallBillingDimension) : WaterfallMeterValue =
     { Total = Quantity.Zero
       Consumption = Map.empty
       LastUpdate = now }

  let setTotal (newTotal: Quantity) (meter: WaterfallMeterValue) : WaterfallMeterValue =
    { meter with Total = newTotal }

  let getModel (waterfallDimension: WaterfallBillingDimension) : WaterfallExpandedModel =
    match waterfallDimension.Model with
    | None -> waterfallDimension.Tiers |> expandToFullModel
    | Some model -> model

  let consume (waterfallDimension: WaterfallBillingDimension) (now: MeteringDateTime) (amount: Quantity) (meter: WaterfallMeterValue) : WaterfallMeterValue =
    waterfallDimension
    |> getModel
    |> findRange meter.Total
    |> List.fold subtract { CurrentTotal = meter.Total; AmountToBeDeducted = amount; Consumption = meter.Consumption }
    |> fun agg ->
        { WaterfallMeterValue.Total = agg.CurrentTotal
          Consumption = agg.Consumption
          LastUpdate = now }

  let accountExpiredSubmission (dimensionId: DimensionId) (waterfallDimension: WaterfallBillingDimension) (now: MeteringDateTime) (amount: Quantity) (meter: WaterfallMeterValue) : WaterfallMeterValue =
    let newConsumption = meter.Consumption |> Map.change dimensionId (add amount)

    { meter with
        Consumption = newConsumption
        LastUpdate = now }

  let newBillingCycle (now: MeteringDateTime) (x: WaterfallBillingDimension) : WaterfallMeterValue =
     { Total = Quantity.Zero
       Consumption = Map.empty
       LastUpdate = now }

  let containsReportableQuantities (this: WaterfallMeterValue) : bool =
    (this.Consumption |> Map.count) > 0

  let closeHour (marketplaceResourceId: MarketplaceResourceId) (planId: PlanId) (this: WaterfallBillingDimension): (MarketplaceRequest list * WaterfallBillingDimension) =
    match this.Meter with
    | None -> (List.empty, this)
    | Some meter ->
        let consumption =
            meter.Consumption
            |> Map.toSeq
            |> Seq.map (fun (dimensionId, quantity) ->
                { MarketplaceResourceId = marketplaceResourceId
                  PlanId = planId
                  DimensionId = dimensionId ; Quantity = quantity
                  EffectiveStartTime = meter.LastUpdate |> MeteringDateTime.beginOfTheHour })
            |> Seq.toList
        let newMeter =  { meter with Consumption = Map.empty }
        (consumption, { this with Meter = Some newMeter })

  let createBillingDimension (tiers: WaterfallIncrementalDescription) meter : WaterfallBillingDimension =
    { Tiers = tiers
      Model = tiers |> expandToFullModel |> Some
      Meter = meter }