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

type WaterfallModel = 
    WaterfallModelRow list

type WaterfallMeterValue =
  { Model: WaterfallModel 
    Total: Quantity
    Consumption: Map<DimensionId, Quantity>
    LastUpdate: MeteringDateTime }

/// These must be reported
type ConsumptionReport = 
  { DimensionId: DimensionId
    Quantity: Quantity }

type private SubtractionAggregation =
  { CurrentTotal: Quantity 
    AmountToBeDeducted: Quantity 
    Consumption: Map<DimensionId, Quantity> }
    
module WaterfallModel =
  let expandToFullModel (dimension: WaterfallBillingDimension) : WaterfallModel =
    let len = dimension.Tiers |> List.length

    let expanded =
      [0 .. len - 1]
      |> List.map (fun i -> 
        { Threshold = 
            dimension.Tiers
            |> List.take (i + 1)
            |> List.sumBy (fun x -> x.Threshold)
          DimensionId = 
            dimension.Tiers
            |> List.skip(i)
            |> List.head
            |> fun x -> x.DimensionId })

    let included =
      expanded
      |> List.head
      |> fun x -> x.Threshold
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
  let findRange (amount: Quantity) (model: WaterfallModel) : WaterfallModelRow list =
    /// Determine if the current total matches the given row.
    let isNotInRow (currentTotal: Quantity) (row: WaterfallModelRow) : bool =
        match row with
        | FreeIncluded x -> currentTotal < x
        | Range { LowerIncluding = lower; UpperExcluding = upper } -> lower <= currentTotal && currentTotal < upper
        | Overage { LowerIncluding = lower } -> lower <= currentTotal
        |> not

    model
    |> List.skipWhile (isNotInRow amount)    

  let private subtract (agg: SubtractionAggregation) (row: WaterfallModelRow) : SubtractionAggregation =
    let add (v: Quantity) = function
        | None -> Some v
        | Some e -> Some (v + e)

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

module WaterfallMeterValue =
  let createMeterFromDimension (now: MeteringDateTime) (dimension: WaterfallBillingDimension) : WaterfallMeterValue = 
     { Model = dimension |> WaterfallModel.expandToFullModel
       Total = Quantity.Zero
       Consumption = Map.empty
       LastUpdate = now }

  let setTotal (newTotal: Quantity) (meter: WaterfallMeterValue) : WaterfallMeterValue = 
    { meter with Total = newTotal }

  let consume (now: MeteringDateTime) (amount: Quantity) (meter: WaterfallMeterValue) : WaterfallMeterValue =
    WaterfallModel.findRange meter.Total meter.Model
    |> List.fold WaterfallModel.subtract { CurrentTotal = meter.Total; AmountToBeDeducted = amount; Consumption = meter.Consumption } 
    |> fun agg ->
        { meter with 
            Total = agg.CurrentTotal 
            Consumption = agg.Consumption
            LastUpdate = now }
  
  let newBillingCycle (now: MeteringDateTime) (x: WaterfallBillingDimension) : (ApplicationInternalMeterName * WaterfallMeterValue) =
        (x.ApplicationInternalMeterName, failwith "notimplemented")

  let containsReportableQuantities (this: WaterfallMeterValue) : bool =
    (this.Consumption |> Map.count) > 0

  let closeHour (marketplaceResourceId: MarketplaceResourceId) (plan: Plan) (name: ApplicationInternalMeterName) (this: WaterfallMeterValue): (MarketplaceRequest list * WaterfallMeterValue) =
    let dimension = plan.BillingDimensions.find name
    let model =
        match dimension with
        | WaterfallBillingDimension wbd -> wbd |> WaterfallModel.expandToFullModel
        | _ -> failwith "wrong model"
    let consumption = 
        this.Consumption
        |> Map.toSeq
        |> Seq.map (fun (dimensionId, quantity) -> 
            { MarketplaceResourceId = marketplaceResourceId
              PlanId = plan.PlanId
              DimensionId = dimensionId ; Quantity = quantity
              EffectiveStartTime = this.LastUpdate |> MeteringDateTime.beginOfTheHour })
        |> Seq.toList
    let newMeter = { this with Consumption = Map.empty }
    (consumption, newMeter)
        
    
