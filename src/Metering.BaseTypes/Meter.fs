// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open Metering.BaseTypes.EventHub

type Meter =
    { Subscription: Subscription // The purchase information of the subscription
      InternalMetersMapping: InternalMetersMapping // The table mapping app-internal meter names to 'proper' ones for marketplace
      CurrentMeterValues: CurrentMeterValues // The current meter values in the aggregator
      UsageToBeReported: MarketplaceRequest list // a list of usage elements which haven't yet been reported to the metering API
      LastProcessedMessage: MessagePosition } // Last message which has been applied to this Meter    
        
module Meter =
    let setCurrentMeterValues x this = { this with CurrentMeterValues = x }
    let applyToCurrentMeterValue f this = { this with CurrentMeterValues = (f this.CurrentMeterValues) }
    let setLastProcessedMessage x this = { this with LastProcessedMessage = x }
    let addUsageToBeReported x this = { this with UsageToBeReported = (x :: this.UsageToBeReported) }
    let addUsagesToBeReported x this = { this with UsageToBeReported = List.concat [ x; this.UsageToBeReported ] }
    
    /// Removes the item from the UsageToBeReported collection
    let removeUsageToBeReported x s = { s with UsageToBeReported = (s.UsageToBeReported |> List.filter (fun e -> e <> x)) }

    /// This function must be called when 'a new hour' started, i.e. the previous period must be closed.
    let closePreviousMeteringPeriod (state: Meter) : Meter =
        let isConsumedQuantity = function
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
    
    let updateConsumption (quantity: Quantity) (timestamp: MeteringDateTime) (someDimension: DimensionId option) (currentMeterValues: CurrentMeterValues) : CurrentMeterValues = 
        match quantity.isAllowedIncomingQuantity with
        | false -> 
            // if the incoming value is not a real (non-negative) number, don't change the meter. 
            currentMeterValues
        | true -> 
            match someDimension with 
            | None -> 
                // TODO: Log that an unknown meter was reported
                currentMeterValues
            | Some dimension ->
                if currentMeterValues.value |> Map.containsKey dimension
                then
                    // The meter exists (might be included or overage), so handle properly
                    currentMeterValues.value
                    |> Map.change dimension (MeterValue.someHandleQuantity timestamp quantity)
                    |> CurrentMeterValues.create
                else
                    // No existing meter value, i.e. record as overage
                    let newConsumption = ConsumedQuantity (ConsumedQuantity.create timestamp quantity)

                    currentMeterValues.value
                    |> Map.add dimension newConsumption
                    |> CurrentMeterValues.create

    let handleUsageEvent ((event: InternalUsageEvent), (currentPosition: MessagePosition)) (state : Meter) : Meter =
        let someDimension : DimensionId option = 
            state.InternalMetersMapping.value |> Map.tryFind event.MeterName
        
        let closePreviousIntervalIfNeeded : (Meter -> Meter) = 
            let last = state.LastProcessedMessage.PartitionTimestamp
            let curr = currentPosition.PartitionTimestamp
            match BillingPeriod.previousBillingIntervalCanBeClosedNewEvent last curr with
            | Close -> closePreviousMeteringPeriod
            | KeepOpen -> id

        state
        |> closePreviousIntervalIfNeeded
        |> applyToCurrentMeterValue (updateConsumption event.Quantity currentPosition.PartitionTimestamp someDimension)
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

            let someDimension = Some expired.RequestData.DimensionId
            let handleTooLateSubmissionAsIfTheUsageHappenedNow = 
                updateConsumption expired.RequestData.Quantity messagePosition.PartitionTimestamp someDimension

            { meter with 
                CurrentMeterValues = meter.CurrentMeterValues |> handleTooLateSubmissionAsIfTheUsageHappenedNow
                UsageToBeReported = meter.UsageToBeReported |> List.except [ expired.RequestData ] }
        | Generic _ -> 
            meter
        
    let handleUsageSubmissionToAPI (item: MarketplaceResponse) (messagePosition: MessagePosition) (meter: Meter) : Meter =
        match item.Result with
        | Ok success ->  meter |> removeUsageToBeReported success.RequestData 
        | Error error -> meter |> handleUnsuccessfulMeterSubmission error messagePosition
        
    let topupMonthlyCreditsOnNewSubscription (time: MeteringDateTime) (meter: Meter) : Meter =
        let currentMeters = meter.Subscription.Plan.BillingDimensions.currentMeterValues time 
        setCurrentMeterValues currentMeters meter

    let createNewSubscription (subscriptionCreationInformation: SubscriptionCreationInformation) (messagePosition: MessagePosition) : Meter =
        // When we receive the creation of a subscription
        { Subscription = subscriptionCreationInformation.Subscription
          InternalMetersMapping = subscriptionCreationInformation.InternalMetersMapping
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