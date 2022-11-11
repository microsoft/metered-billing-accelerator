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
    let matches (marketplaceResourceId: MarketplaceResourceId) (this: Meter) : bool =
        this.Subscription.MarketplaceResourceId.Matches(marketplaceResourceId)

    let setCurrentMeterValues x this = { this with CurrentMeterValues = x }
    let applyToCurrentMeterValue (f: CurrentMeterValues -> CurrentMeterValues) (this: Meter) : Meter = { this with CurrentMeterValues = (f this.CurrentMeterValues) } // 
    let setLastProcessedMessage x this = { this with LastProcessedMessage = x }
    let addUsageToBeReported (item: MarketplaceRequest) this = { this with UsageToBeReported = (item :: this.UsageToBeReported) }
    let addUsagesToBeReported (items: MarketplaceRequest list) this = { this with UsageToBeReported = List.concat [ items; this.UsageToBeReported ] }
    /// Removes the item from the UsageToBeReported collection
    let removeUsageToBeReported (item: MarketplaceRequest) this = { this with UsageToBeReported = (this.UsageToBeReported |> List.filter (fun e -> e <> item)) }
    
    /// This function must be called when 'a new hour' started, i.e. the previous period must be closed.
    let closePreviousMeteringPeriod (state: Meter) : Meter =
        let closeHour = MeterValue.closeHour state.Subscription.MarketplaceResourceId state.Subscription.Plan

        let results : (ApplicationInternalMeterName * (MarketplaceRequest list * MeterValue)) seq =
            state.CurrentMeterValues.value
            |> Map.toSeq
            |> Seq.map (fun (name, meterValue) -> (name, closeHour name meterValue))
        
        let usagesToBeReported = results |> Seq.map (fun (_name,(marketplaceRequests,_newMeter)) -> marketplaceRequests) |> List.concat
        let newMeters = results |> Seq.map (fun (name,(_marketplaceRequests,newMeter)) -> (name,newMeter)) |> Map.ofSeq |> CurrentMeterValues.create

        state
        |> setCurrentMeterValues newMeters
        |> addUsagesToBeReported usagesToBeReported
    

    //member static updateConsumptionForDimensionId (quantity: Quantity) (timestamp: MeteringDateTime) (dimensionId: DimensionId) (currentMeterValues: CurrentMeterValues) : CurrentMeterValues =
    //    if currentMeterValues.value |> Map.containsKey dimensionId
    //    then
    //        // The meter exists (might be included or overage), so handle properly
    //        currentMeterValues.value
    //        |> Map.change dimensionId (SimpleMeterValue.someHandleQuantity timestamp quantity)
    //        |> CurrentMeterValues.create
    //    else
    //        // No existing meter value, i.e. record as overage
    //        let newConsumption = ConsumedQuantity (ConsumedQuantity.create timestamp quantity)
    //    
    //        currentMeterValues.value
    //        |> Map.add dimensionId newConsumption
    //        |> CurrentMeterValues.create



    let updateConsumptionForApplicationInternalMeterName (quantity: Quantity) (timestamp: MeteringDateTime) (applicationInternalMeterName: ApplicationInternalMeterName) (billingDimensions: BillingDimensions) (currentMeterValues: CurrentMeterValues) : CurrentMeterValues = 
        if not quantity.isAllowedIncomingQuantity 
        then currentMeterValues // If the incoming value is not a real (non-negative) number, don't change anything.
        else            
            let someDimension = billingDimensions.value |> List.tryFind (fun x -> x |> BillingDimension.applicationInternalMeterName = applicationInternalMeterName)
            match someDimension with 
            | None -> currentMeterValues // TODO: Log that an unknown meter was reported
            | Some billingDimension ->
                let createOrUpdate : (MeterValue option -> MeterValue option) = function
                    | None ->
                        let (_, newMeterValue) = MeterValue.newBillingCycle timestamp billingDimension
                        Some newMeterValue
                    | Some meterValue -> 
                        meterValue
                        |> MeterValue.applyConsumption timestamp quantity
                        |> Some
                
                currentMeterValues.value
                |> Map.change applicationInternalMeterName createOrUpdate
                |> CurrentMeterValues.create

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

            // Now we need to find the correct meter to re-apply the consumption to.
            let theRightDimension : (BillingDimension -> bool) = BillingDimension.hasDimensionId expired.RequestData.DimensionId
            match meter.Subscription.Plan.BillingDimensions.value |> List.tryFind theRightDimension with
            | None -> 
                // It seems the dimension in question isn't part of the plan, that is impossible
                meter
            | Some x -> 
                let applicationInternalMeterName = x |> BillingDimension.applicationInternalMeterName
            
                let handleTooLateSubmissionAsIfTheUsageHappenedNow =
                    updateConsumptionForApplicationInternalMeterName 
                        expired.RequestData.Quantity
                        messagePosition.PartitionTimestamp
                        applicationInternalMeterName
                        meter.Subscription.Plan.BillingDimensions

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
        let refreshedStartOfMonthMeters : CurrentMeterValues = 
            meter.Subscription.Plan.BillingDimensions
            |> CurrentMeterValues.createIncludedQuantitiesForNewBillingCycle now 

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