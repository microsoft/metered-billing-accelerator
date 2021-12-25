namespace Metering.Types

open Metering.Types.EventHub

type Meter =
    { Subscription: Subscription // The purchase information of the subscription
      InternalMetersMapping: InternalMetersMapping // The table mapping app-internal meter names to 'proper' ones for marketplace
      CurrentMeterValues: CurrentMeterValues // The current meter values in the aggregator
      UsageToBeReported: MarketplaceRequest list // a list of usage elements which haven't yet been reported to the metering API
      LastProcessedMessage: MessagePosition } // Last message which has been applied to this Meter
        
module Meter =
    let setCurrentMeterValues x s = { s with CurrentMeterValues = x }
    let applyToCurrentMeterValue f s = { s with CurrentMeterValues = (f s.CurrentMeterValues) }
    let setLastProcessedMessage x s = { s with LastProcessedMessage = x }
    let addUsageToBeReported x s = { s with UsageToBeReported = (x :: s.UsageToBeReported) }
    let addUsagesToBeReported x s = { s with UsageToBeReported = List.concat [ x; s.UsageToBeReported ] }
    
    /// Removes the item from the UsageToBeReported collection
    let removeUsageToBeReported x s = { s with UsageToBeReported = (s.UsageToBeReported |> List.filter (fun e -> e <> x)) }

    let closePreviousMeteringPeriod (config: MeteringConfigurationProvider) (state: Meter) : Meter =
        let isConsumedQuantity = function
            | ConsumedQuantity _ -> true
            | _ -> false

        // From the state.CurrentMeterValues, remove all those which are about consumption, and turn that into an API call
        let (consumedValues, includedValuesWhichDontNeedToBeReported) = 
            state.CurrentMeterValues
            |> Map.partition (fun _ -> isConsumedQuantity)

        let usagesToBeReported = 
            consumedValues
            |> Map.map (fun dimensionId cq -> 
                match cq with 
                | IncludedQuantity _ -> failwith "cannot happen"
                | ConsumedQuantity q -> 
                    { ResourceId = state.Subscription.InternalResourceId
                      Quantity = q.Amount
                      PlanId = state.Subscription.Plan.PlanId 
                      DimensionId = dimensionId
                      EffectiveStartTime = state.LastProcessedMessage.PartitionTimestamp |> MeteringDateTime.beginOfTheHour } )
            |> Map.values
            |> List.ofSeq

        state
        |> setCurrentMeterValues includedValuesWhichDontNeedToBeReported
        |> addUsagesToBeReported usagesToBeReported
    
    let handleUsageEvent (config: MeteringConfigurationProvider) ((event: InternalUsageEvent), (currentPosition: MessagePosition)) (state : Meter) : Meter =
        let updateConsumption (currentMeterValues: CurrentMeterValues) : CurrentMeterValues = 
            let someDimension : DimensionId option = 
                state.InternalMetersMapping |> InternalMetersMapping.value |> Map.tryFind event.MeterName
            
            match event.Quantity |> Quantity.isAllowedIncomingQuantity with
            | false -> 
                // if the incoming value is not a real (non-negative) number, don't change the meter. 
                currentMeterValues
            | true -> 
                match someDimension with 
                | None -> 
                    // TODO: Log that an unknown meter was reported
                    currentMeterValues
                | Some dimension ->
                    if currentMeterValues |> Map.containsKey dimension
                    then 
                        currentMeterValues
                        |> Map.change dimension (MeterValue.someHandleQuantity event.Quantity currentPosition)
                    else
                        let newConsumption = ConsumedQuantity (ConsumedQuantity.create event.Timestamp event.Quantity)
                        currentMeterValues
                        |> Map.add dimension newConsumption
        
        let closePreviousIntervalIfNeeded : (Meter -> Meter) = 
            let last = state.LastProcessedMessage.PartitionTimestamp
            let curr = currentPosition.PartitionTimestamp
            match BillingPeriod.previousBillingIntervalCanBeClosedNewEvent last curr with
            | Close -> closePreviousMeteringPeriod config
            | KeepOpen -> id

        state
        |> closePreviousIntervalIfNeeded
        |> applyToCurrentMeterValue updateConsumption
        |> setLastProcessedMessage currentPosition // Todo: Decide where to update the position

    let handleAggregatorCatchedUp (config: MeteringConfigurationProvider) (meter: Meter) : Meter =
        match BillingPeriod.previousBillingIntervalCanBeClosedWakeup (config.CurrentTimeProvider(), config.GracePeriod) meter.LastProcessedMessage.PartitionTimestamp  with
        | Close -> meter |> closePreviousMeteringPeriod config
        | KeepOpen -> meter

    let handleUnsuccessfulMeterSubmission (config: MeteringConfigurationProvider) (error: MarketplaceSubmissionError) (meter: Meter) : Meter =
        match error with
        | DuplicateSubmission duplicate -> 
            meter |> removeUsageToBeReported duplicate.PreviouslyAcceptedMessage.RequestData
        | ResourceNotFound notFound -> 
            // TODO When the resource doesn't exist in marketplace, we need to raise some alarm bells here.
            meter |> removeUsageToBeReported notFound.RequestData
        | Expired expired -> 
            // Seems we're trying to submit something which is too old. 
            // Need to ring an alarm that the aggregator must be scheduled more frequently
            // Submit compensating action for now?
            { meter with 
                UsageToBeReported = meter.UsageToBeReported |> List.except [ expired.RequestData ] }
        | Generic generic -> 
            meter
        
    let handleUsageSubmissionToAPI (config: MeteringConfigurationProvider) (item: MarketplaceResponse) (meter: Meter) : Meter =
        match item.Result with
        | Ok success ->  meter |> removeUsageToBeReported success.RequestData 
        | Error error -> meter |> handleUnsuccessfulMeterSubmission config error 
        
    let topupMonthlyCreditsOnNewSubscription (time: MeteringDateTime) (meter: Meter) : Meter =
        meter
        |> setCurrentMeterValues (meter.Subscription.Plan.BillingDimensions |> BillingDimension.createIncludedQuantityForNow time)

    let createNewSubscription (subscriptionCreationInformation: SubscriptionCreationInformation) (messagePosition: MessagePosition) : Meter =
        // When we receive the creation of a subscription
        { Subscription = subscriptionCreationInformation.Subscription
          InternalMetersMapping = subscriptionCreationInformation.InternalMetersMapping
          LastProcessedMessage = messagePosition
          CurrentMeterValues = Map.empty
          UsageToBeReported = List.empty }
        |> topupMonthlyCreditsOnNewSubscription messagePosition.PartitionTimestamp

    let toStr (pid: string) (m: Meter) =
        let mStr =
            m.CurrentMeterValues
            |> CurrentMeterValues.toStr
            |> Seq.map(fun v -> $"{pid} {m.Subscription.InternalResourceId |> InternalResourceId.toStr}: {v}")
            |> String.concat "\n"

        let uStr =
            m.UsageToBeReported
            |> Seq.map MarketplaceRequest.toStr
            |> String.concat "\n"

        $"{mStr}\n{uStr}"