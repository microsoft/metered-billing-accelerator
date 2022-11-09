// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes.WaterfallTypes

open Metering.BaseTypes

/// Serialization
type WaterfallDescriptionItem = 
    { Threshold: Quantity
      DimensionId: DimensionId }

type WaterfallDescription =
    { /// Application-internal name of the meter / billing dimension. 
      InternalName: ApplicationInternalMeterName

      /// The dimension as Marketplace knows it.
      Tiers: WaterfallDescriptionItem list}

type Range =
    { DimensionId: DimensionId
      LowerIncluding: Quantity
      UpperExcluding: Quantity }

type Overage =
    { DimensionId: DimensionId
      LowerIncluding: Quantity }

/// This is the 'compiled model'
type WaterfallModelRow =  
  | FreeIncluded of Quantity // For included quantities, no reporting to the metering API
  | Range of Range
  | Overage of Overage

type WaterfallModel = 
  WaterfallModelRow list

type WaterfallMeterValue =
  { Model: WaterfallModel 
    Total: Quantity
    Consumption: Map<DimensionId, Quantity> }

/// These must be reported
type ConsumptionReport = 
  { DimensionId: DimensionId
    Quantity: Quantity }

type SubtractionAggregation =
  { CurrentTotal: Quantity 
    AmountToBeDeducted: Quantity 
    Consumption: Map<DimensionId, Quantity> }
    
module WaterfallModel =
  let expand (model: WaterfallDescription) : WaterfallModel =
    let len = model.Tiers |> List.length

    let expanded =
      [0 .. len - 1]
      |> List.map (fun i -> 
        { Threshold = 
            model.Tiers
            |> List.take (i + 1)
            |> List.sumBy (fun x -> x.Threshold)
          DimensionId = 
            model.Tiers
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

  let subtract (agg: SubtractionAggregation) (row: WaterfallModelRow) : SubtractionAggregation =
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

type WaterfallMeter =
  { Model: WaterfallModel 
    Total: Quantity
    Consumption: Map<DimensionId, Quantity> }

module WaterfallMeter =
  let create (description: WaterfallDescription) : WaterfallMeter = 
     { Model = description |>  WaterfallModel.expand
       Total = Quantity.Zero
       Consumption = Map.empty }

  let setTotal (newTotal: Quantity) (meter: WaterfallMeter) : WaterfallMeter = 
    { meter with Total = newTotal }

  let consume (amount: Quantity) (meter: WaterfallMeter) : WaterfallMeter =
    WaterfallModel.findRange meter.Total meter.Model
    |> List.fold WaterfallModel.subtract { CurrentTotal = meter.Total; AmountToBeDeducted = amount; Consumption = meter.Consumption } 
    |> fun agg ->
        { meter with 
            Total = agg.CurrentTotal 
            Consumption = agg.Consumption }