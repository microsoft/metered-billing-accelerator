// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

type ConsumedQuantity =
    { /// The consumed quantity in the current hour.
      CurrentHour: Quantity

      /// The consumed total quantity in the current billing period.
      BillingPeriodTotal: Quantity
      LastUpdate: MeteringDateTime }

    override this.ToString() = sprintf "%s consumed this hour, %s in total" (this.CurrentHour.ToString()) (this.BillingPeriodTotal.ToString())

    member this.increaseConsumption now amount = { this with CurrentHour = this.CurrentHour + amount; BillingPeriodTotal = this.BillingPeriodTotal + amount; LastUpdate = now }

    member this.resetHourCounter now = { this with CurrentHour = Quantity.Zero ; LastUpdate = now }

    static member createNew now currentHourAmount = { CurrentHour = currentHourAmount; BillingPeriodTotal = currentHourAmount; LastUpdate = now }

type IncludedQuantity =
    { RemainingQuantity: Quantity
      BillingPeriodTotal: Quantity
      LastUpdate: MeteringDateTime }

    override this.ToString() = sprintf "Remaining %s" (this.RemainingQuantity.ToString())

    member this.decrease now amount = { this with RemainingQuantity = this.RemainingQuantity - amount; BillingPeriodTotal = this.BillingPeriodTotal + amount; LastUpdate = now }

    //static member createNew now remainingQuantity = { RemainingQuantity = remainingQuantity; BillingPeriodTotal = Quantity.Zero; LastUpdate = now }

type SimpleMeterValue =
    | ConsumedQuantity of ConsumedQuantity
    | IncludedQuantity of IncludedQuantity

    override this.ToString() =
        match this with
        | ConsumedQuantity cq -> cq.ToString()
        | IncludedQuantity iq -> iq.ToString()

/// The 'simple consumption' represents the most simple Azure Marketplace metering model.
/// There is a dimension with included quantities, and once the included quantities are consumed, the overage starts counting.
type SimpleBillingDimension =
    { DimensionId: DimensionId

      /// The dimensions configured
      IncludedQuantity: Quantity

      Meter: SimpleMeterValue option }

module SimpleMeterLogic =
    /// Subtracts the given Quantity from a MeterValue
    let subtractQuantity (now: MeteringDateTime) (quantity: Quantity) (this: SimpleMeterValue) : SimpleMeterValue =
        this
        |> function
           | ConsumedQuantity consumedQuantity ->
                consumedQuantity.increaseConsumption now quantity
                |> ConsumedQuantity
           | IncludedQuantity iq ->
                let remaining = iq.RemainingQuantity

                if remaining >= quantity
                then
                    iq.decrease now quantity
                    |> IncludedQuantity
                else
                    quantity - remaining
                    |> ConsumedQuantity.createNew now
                    |> ConsumedQuantity

    let createIncluded (now: MeteringDateTime) (quantity: Quantity) : SimpleMeterValue =
        IncludedQuantity { RemainingQuantity = quantity; BillingPeriodTotal = Quantity.Zero; LastUpdate = now }

    let someHandleQuantity (currentPosition: MeteringDateTime) (quantity: Quantity) (current: SimpleMeterValue option) : SimpleMeterValue option =
        let subtract quantity (meterValue: SimpleMeterValue) =
            meterValue |> subtractQuantity currentPosition quantity

        current
        |> Option.bind ((subtract quantity) >> Some)

    let newBillingCycle (now: MeteringDateTime) (x: SimpleBillingDimension) : SimpleMeterValue =
        IncludedQuantity { RemainingQuantity = x.IncludedQuantity; BillingPeriodTotal = Quantity.Zero; LastUpdate = now }

    let containsReportableQuantities (this: SimpleMeterValue) : bool =
        match this with
        | ConsumedQuantity q when q.CurrentHour > Quantity.Zero -> true
        | _ -> false

    let closeHour marketplaceResourceId (planId: PlanId) (this: SimpleBillingDimension) : (MarketplaceRequest list * SimpleBillingDimension) =
        match this.Meter with
        | None -> (List.empty, this)
        | Some meter ->
            match meter with
            | IncludedQuantity _ -> (List.empty, this)
            | ConsumedQuantity q when q.CurrentHour = Quantity.Zero -> (List.empty, this)
            | ConsumedQuantity q ->
                let marketplaceRequest =
                    { MarketplaceResourceId = marketplaceResourceId
                      PlanId = planId
                      DimensionId = this.DimensionId
                      Quantity = q.CurrentHour
                      EffectiveStartTime = q.LastUpdate |> MeteringDateTime.beginOfTheHour }

                let newMeterValue = q.resetHourCounter (MeteringDateTime.now()) |> ConsumedQuantity
                ( [ marketplaceRequest ], { this with Meter = Some newMeterValue })

    //let createNewMeterValueDuringBillingPeriod (now: MeteringDateTime) (quantity: Quantity) : SimpleMeterValue =
    //    ConsumedQuantity.create now quantity
    //    |> ConsumedQuantity
