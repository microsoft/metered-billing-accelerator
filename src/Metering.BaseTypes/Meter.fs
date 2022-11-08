// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open Metering.BaseTypes.EventHub

type Meter =
    { Subscription: Subscription // The purchase information of the subscription
      CurrentMeterValues: CurrentMeterValues // The current meter values in the aggregator
      UsageToBeReported: MarketplaceRequest list // a list of usage elements which haven't yet been reported to the metering API
      LastProcessedMessage: MessagePosition } // Last message which has been applied to this Meter    
        
module Meter =
    let setCurrentMeterValues x this = { this with CurrentMeterValues = x }
    let applyToCurrentMeterValue (f: CurrentMeterValues -> CurrentMeterValues) (this: Meter) : Meter = { this with CurrentMeterValues = (f this.CurrentMeterValues) } // 
    let setLastProcessedMessage x this = { this with LastProcessedMessage = x }
    let addUsageToBeReported x this = { this with UsageToBeReported = (x :: this.UsageToBeReported) }
    let addUsagesToBeReported x this = { this with UsageToBeReported = List.concat [ x; this.UsageToBeReported ] }
    
    let matches (marketplaceResourceId: MarketplaceResourceId) (meter: Meter) : bool =
        meter.Subscription.MarketplaceResourceId.Matches(marketplaceResourceId)

    /// Removes the item from the UsageToBeReported collection
    let removeUsageToBeReported x s = { s with UsageToBeReported = (s.UsageToBeReported |> List.filter (fun e -> e <> x)) }

    /// This function must be called when 'a new hour' started, i.e. the previous period must be closed.
    let closePreviousMeteringPeriod (state: Meter) : Meter =
        let isConsumedQuantity : (SimpleMeterValue -> bool) = function
            | ConsumedQuantity _ -> true
            | _ -> false

        // From the state.CurrentMeterValues, remove all those which are about consumption, and turn them into an API call
        let (consumedValues, includedValuesWhichDontNeedToBeReported) = 
            state.CurrentMeterValues.value
            |> Map.partition (fun _ -> isConsumedQuantity)

        let usagesToBeReported = 
            consumedValues
            |> Map.map (fun dimensionId cq -> 
                match cq with 
                | IncludedQuantity _ -> failwith "cannot happen"
                | ConsumedQuantity q -> 
                    { MarketplaceResourceId = state.Subscription.MarketplaceResourceId
                      Quantity = q.Amount
                      PlanId = state.Subscription.Plan.PlanId 
                      DimensionId = dimensionId
                      EffectiveStartTime = state.LastProcessedMessage.PartitionTimestamp |> MeteringDateTime.beginOfTheHour } )
            |> Map.values
            |> List.ofSeq

        state
        |> setCurrentMeterValues (includedValuesWhichDontNeedToBeReported |> CurrentMeterValues.create)
        |> addUsagesToBeReported usagesToBeReported
    
    let updateConsumptionForDimensionId (quantity: Quantity) (timestamp: MeteringDateTime) (dimensionId: DimensionId) (currentMeterValues: CurrentMeterValues) : CurrentMeterValues =
        if currentMeterValues.value |> Map.containsKey dimensionId
        then
            // The meter exists (might be included or overage), so handle properly
            currentMeterValues.value
            |> Map.change dimensionId (SimpleMeterValue.someHandleQuantity timestamp quantity)
            |> CurrentMeterValues.create
        else
            // No existing meter value, i.e. record as overage
            let newConsumption = ConsumedQuantity (ConsumedQuantity.create timestamp quantity)

            currentMeterValues.value
            |> Map.add dimensionId newConsumption
            |> CurrentMeterValues.create

    let updateConsumptionForApplicationInternalMeterName (quantity: Quantity) (timestamp: MeteringDateTime) (meterName: ApplicationInternalMeterName) (billingDimensions: BillingDimensions) (currentMeterValues: CurrentMeterValues) : CurrentMeterValues = 
        match quantity.isAllowedIncomingQuantity with
        | false -> 
            // if the incoming value is not a real (non-negative) number, don't change the meter. 
            currentMeterValues
        | true -> 
            let someDimension = billingDimensions.value |> List.tryFind (fun x -> x.InternalName = meterName)
            match someDimension with 
            | None -> 
                // TODO: Log that an unknown meter was reported
                currentMeterValues
            | Some dimension ->
                currentMeterValues
                |> updateConsumptionForDimensionId quantity timestamp dimension.DimensionId

    let handleUsageEvent ((event: InternalUsageEvent), (currentPosition: MessagePosition)) (meter : Meter) : Meter =        
        let closePreviousIntervalIfNeeded : (Meter -> Meter) = 
            let last = meter.LastProcessedMessage.PartitionTimestamp
            let curr = currentPosition.PartitionTimestamp
            match BillingPeriod.previousBillingIntervalCanBeClosedNewEvent last curr with
            | Close -> closePreviousMeteringPeriod
            | KeepOpen -> id
        
        let transformCurrentMeterValues : (CurrentMeterValues -> CurrentMeterValues) = 
            updateConsumptionForApplicationInternalMeterName event.Quantity currentPosition.PartitionTimestamp event.MeterName meter.Subscription.Plan.BillingDimensions

        meter
        |> closePreviousIntervalIfNeeded
        |> applyToCurrentMeterValue transformCurrentMeterValues
        |> setLastProcessedMessage currentPosition // Todo: Decide where to update the position

    let closePreviousHourIfNeeded (partitionTimestamp: MeteringDateTime) (meter: Meter) : Meter =
        let previousTimestamp = meter.LastProcessedMessage.PartitionTimestamp

        match BillingPeriod.previousBillingIntervalCanBeClosedNewEvent previousTimestamp partitionTimestamp with
        | Close -> meter |> closePreviousMeteringPeriod
        | KeepOpen -> meter

    let handleUnsuccessfulMeterSubmission (error: MarketplaceSubmissionError) (messagePosition: MessagePosition) (meter: Meter) : Meter =
        match error with
        | DuplicateSubmission duplicate -> 
            meter |> removeUsageToBeReported duplicate.PreviouslyAcceptedMessage.RequestData
        | ResourceNotFound notFound -> 
            // TODO When the resource doesn't exist in marketplace, we need to raise some alarm bells here.
            meter |> removeUsageToBeReported notFound.RequestData
        | Expired expired -> 
            // Seems we're trying to submit something which is too old. 
            // Need to ring an alarm that the aggregator must be scheduled more frequently
            // TODO: Submit compensating action for now?

            let handleTooLateSubmissionAsIfTheUsageHappenedNow = 
                updateConsumptionForDimensionId expired.RequestData.Quantity messagePosition.PartitionTimestamp expired.RequestData.DimensionId

            { meter with 
                CurrentMeterValues = meter.CurrentMeterValues |> handleTooLateSubmissionAsIfTheUsageHappenedNow
                UsageToBeReported = meter.UsageToBeReported |> List.except [ expired.RequestData ] }
        | Generic _ -> 
            meter
        
    let handleUsageSubmissionToAPI (item: MarketplaceResponse) (messagePosition: MessagePosition) (meter: Meter) : Meter =
        match item.Result with
        | Ok success ->  meter |> removeUsageToBeReported success.RequestData 
        | Error error -> meter |> handleUnsuccessfulMeterSubmission error messagePosition
        
    let topupMonthlyCreditsOnNewSubscription (now: MeteringDateTime) (meter: Meter) : Meter =
        let refreshedStartOfMonthMeters = 
            meter.Subscription.Plan.BillingDimensions.value
            |> SimpleConsumptionBillingDimension.createIncludedQuantitiesForNewBillingCycle now 

        meter
        |> setCurrentMeterValues refreshedStartOfMonthMeters 

    let createNewSubscription (subscriptionCreationInformation: SubscriptionCreationInformation) (messagePosition: MessagePosition) : Meter =
        // When we receive the creation of a subscription
        { Subscription = subscriptionCreationInformation.Subscription
          LastProcessedMessage = messagePosition
          CurrentMeterValues = Map.empty |> CurrentMeterValues.create
          UsageToBeReported = List.empty }
        |> topupMonthlyCreditsOnNewSubscription messagePosition.PartitionTimestamp

    let toStr (pid: string) (m: Meter) =
        let mStr =
            m.CurrentMeterValues.toStrings
            |> Seq.map(fun v -> $"{pid} {m.Subscription.MarketplaceResourceId.ToString()}: {v}")
            |> String.concat "\n"

        let uStr =
            m.UsageToBeReported
            |> Seq.map (fun a -> a.ToString())
            |> String.concat "\n"

        $"{mStr}\n{uStr}"