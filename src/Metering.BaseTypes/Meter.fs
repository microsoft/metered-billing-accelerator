// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open NodaTime
open Metering.BaseTypes.EventHub

type Meter =
    { Subscription: Subscription // The purchase information of the subscription
      UsageToBeReported: MarketplaceRequest list // a list of usage elements which haven't yet been reported to the metering API
      LastProcessedMessage: MessagePosition // Last message which has been applied to this Meter
      DeletionRequested: bool }

module Meter =
    let matches (marketplaceResourceId: MarketplaceResourceId) (this: Meter) : bool =
        this.Subscription.MarketplaceResourceId.Matches(marketplaceResourceId)

    let notDeletionRequested (this: Meter) : bool =
        not this.DeletionRequested

    let matchesAndNotDeletionRequested (marketplaceResourceId: MarketplaceResourceId) (meter: Meter) : bool =
        matches marketplaceResourceId meter && notDeletionRequested meter

    let updateBillingDimensions (billingDimensions: BillingDimensions) (meter: Meter) : Meter =
        { meter with Subscription = meter.Subscription |> Subscription.updateBillingDimensions billingDimensions }

    /// add a usage to the head of the list of usages to be reported.
    let addUsageToBeReported (item: MarketplaceRequest) (meter: Meter) : Meter =
        { meter with UsageToBeReported = (item :: meter.UsageToBeReported) }

    let addUsagesToBeReported (items: MarketplaceRequest list) (meter: Meter) : Meter =
        { meter with UsageToBeReported = List.concat [ items; meter.UsageToBeReported ] }

    let addNewConsumption (quantity: Quantity) (messagePosition: MessagePosition) (applicationInternalMeterName: ApplicationInternalMeterName) (mechanism: MessagePosition -> Quantity -> BillingDimension -> BillingDimension) (meter: Meter) : Meter =
        if not quantity.isAllowedIncomingQuantity
        then
            // If the incoming value is not a real and non-negative number, don't change anything.
            meter
        else
            let update : (BillingDimension -> BillingDimension) = mechanism messagePosition quantity

            // In the current billing dimentions, apply the update to the billing dimension named by applicationInternalMeterName.
            let updatedDimensions =
                meter.Subscription.Plan.BillingDimensions
                |> Map.change applicationInternalMeterName (Option.map update)

            meter
            |> updateBillingDimensions updatedDimensions

    let previousHourCanBeClosed (previous: MeteringDateTime) (now: MeteringDateTime) : bool =
        // When the hour value is different or more than one hour has passed
        (previous.Hour <> now.Hour) || ((now - previous) >= Duration.FromHours(1.0))

    /// This function must be called when 'a new hour' started, or when a meter is deleted, i.e. the current hour must be closed.
    let ``closeHour!`` (now: MeteringDateTime) (meter: Meter) : Meter =
        let resourceId = meter.Subscription.MarketplaceResourceId
        let planId = meter.Subscription.Plan.PlanId

        let results : (ApplicationInternalMeterName * (MarketplaceRequest list * BillingDimension)) seq =
            meter.Subscription.Plan.BillingDimensions
            |> Map.toSeq
            |> Seq.map (fun (name, billingDimension) -> (name, MeterValue.closeHour resourceId planId now name billingDimension))

        let updatedBillingDimensions =
            results
            |> Seq.map (fun (name,(_marketplaceRequests, newMeter)) -> (name, newMeter))
            |> Map.ofSeq

        let usagesToBeReported =
            results
            |> Seq.map (fun (_name, (marketplaceRequests, _newMeter)) -> marketplaceRequests)
            |> List.concat

        meter
        |> updateBillingDimensions updatedBillingDimensions
        |> addUsagesToBeReported usagesToBeReported

    let closePreviousHourIfNeeded (messagePosition: MessagePosition) (meter: Meter) : Meter =
        let now = messagePosition.PartitionTimestamp
        let previous = meter.LastProcessedMessage.PartitionTimestamp

        if previousHourCanBeClosed previous now
        then meter |> ``closeHour!`` now
        else meter

    let addConsumption (quantity: Quantity) (messagePosition: MessagePosition) (applicationInternalMeterName: ApplicationInternalMeterName) (mechanism: MessagePosition -> Quantity -> BillingDimension -> BillingDimension) (meter: Meter) : Meter =
        meter
        |> closePreviousHourIfNeeded messagePosition
        |> addNewConsumption quantity messagePosition applicationInternalMeterName mechanism

    let handleUsageEvent ((event: InternalUsageEvent), (currentPosition: MessagePosition)) (meter : Meter) : Meter =
        meter
        |> addConsumption event.Quantity currentPosition event.MeterName MeterValue.applyConsumption

    let private removeUsageForMarketplaceSuccessResponse (successfulResponse: MarketplaceSuccessResponse) (messagePosition: MessagePosition) (meter: Meter) : Meter =
        let exceptTheRequest = (fun usageToBeReported -> usageToBeReported <> successfulResponse.RequestData)

        { meter with UsageToBeReported = meter.UsageToBeReported |> List.filter exceptTheRequest }

    type UsageReportedRemovalResult =
        | UsageReportRemovedFromMeter
        | UsageReportNotFound

    let handleUsageSubmissionToAPI (item: MarketplaceResponse) (messagePosition: MessagePosition) (meter: Meter) : Meter =
        let removeUsageForMarketplaceSubmissionError (submissionError: MarketplaceSubmissionError) (meter: Meter) : (UsageReportedRemovalResult * Meter) =
            let failedRequest: MarketplaceRequest = MarketplaceSubmissionResult.requestFromError submissionError

            if meter.UsageToBeReported |> List.contains failedRequest
            then (UsageReportRemovedFromMeter, { meter with UsageToBeReported = meter.UsageToBeReported |> List.filter (fun x -> x <> failedRequest )})
            else (UsageReportNotFound, meter)

        let handleUnsuccessfulMeterSubmission (submissionError: MarketplaceSubmissionError) (messagePosition: MessagePosition) (meter: Meter) : Meter =
            match submissionError with
            | DuplicateSubmission duplicate ->
                let (_status, meter) = removeUsageForMarketplaceSubmissionError submissionError meter
                meter
            | ResourceNotFound _ ->
                let (_status, meter) = removeUsageForMarketplaceSubmissionError submissionError meter
                // TODO When the resource doesn't exist in marketplace, we need to raise some alarm bells here.
                meter
            | Expired expired ->
                // Seems we're trying to submit a report which is older than 24 hours.
                // Need to ring an alarm that the aggregator must be scheduled more frequently
                match removeUsageForMarketplaceSubmissionError submissionError meter with
                | (UsageReportNotFound, meter) ->
                    // if the usage was no longer in the meter, no need to do anything
                    meter
                | (UsageReportRemovedFromMeter, meter) ->
                    // Now we need to find the correct meter to re-apply the consumption to.
                    // TODO: Submit compensating action for now?
                    let dimensionIdOfTheFailedSubmission = expired.RequestData.DimensionId
                    let theRightDimension (_, bd: BillingDimension) : bool =
                        BillingDimension.hasDimensionId dimensionIdOfTheFailedSubmission bd

                    let nameAndDimension : (ApplicationInternalMeterName * BillingDimension) option =
                        meter.Subscription.Plan.BillingDimensions
                        |> Map.toSeq
                        |> Seq.tryFind theRightDimension

                    match nameAndDimension with
                    | None -> meter // It seems the dimension in question isn't part of the plan, that is impossible
                    | Some (applicationInternalMeterName, _billingDimension) ->
                        // The quantity should have been reported a long time ago. Unfortunately, the aggregator didn't submit the report in time.
                        // Now we compensate, by pretending as if the the usage happened *now*. Given that the usage is already reflected in the total,
                        // we want to get it recorded without updating the total value.
                        //
                        let applyWithoutUpdatingTotal = MeterValue.accountForExpiredSubmission expired.RequestData.DimensionId
                        addConsumption expired.RequestData.Quantity messagePosition applicationInternalMeterName applyWithoutUpdatingTotal meter
            | Generic _ ->
                meter

        match item.Result with
        | Ok success ->  meter |> removeUsageForMarketplaceSuccessResponse success messagePosition
        | Error error -> meter |> handleUnsuccessfulMeterSubmission error messagePosition

    /// Applies the updateBillingDimension function to each BillingDimension
    let applyUpdateToBillingDimensionsInMeter (update: BillingDimension -> BillingDimension) (meter: Meter) : Meter =
        let updatedDimensions = BillingDimensions.update update meter.Subscription.Plan.BillingDimensions

        meter
        |> updateBillingDimensions updatedDimensions

    let private startNewBillingCycle (now: MeteringDateTime) (meter: Meter) : Meter =
        let newCycle: (BillingDimension -> BillingDimension) = BillingDimension.newBillingCycle now
        applyUpdateToBillingDimensionsInMeter newCycle meter

    let resetCountersIfNewBillingCycleStarted (messagePosition: MessagePosition) (meter: Meter) : Meter =
        let newBillingCycleStarted : bool =
            Subscription.areDifferentBillingCycles
                meter.LastProcessedMessage.PartitionTimestamp
                messagePosition.PartitionTimestamp
                meter.Subscription

        if newBillingCycleStarted
        then meter |> startNewBillingCycle messagePosition.PartitionTimestamp
        else meter

    let touchMeter (messagePosition: MessagePosition) (meter: Meter) : Meter =
        meter
        |> closePreviousHourIfNeeded messagePosition
        |> resetCountersIfNewBillingCycleStarted messagePosition

    let createNewSubscription (subscriptionCreationInformation: SubscriptionCreationInformation) (messagePosition: MessagePosition) : Meter =
        // When we receive the creation of a subscription
        { Subscription = subscriptionCreationInformation.Subscription
          UsageToBeReported = List.empty
          DeletionRequested = false
          LastProcessedMessage = messagePosition }
        |> startNewBillingCycle messagePosition.PartitionTimestamp

    //let applyToCurrentMeterValue (f: CurrentMeterValues -> CurrentMeterValues) (this: Meter) : Meter = { this with CurrentMeterValues = (f this.CurrentMeterValues) }
    let setLastProcessedMessage (messagePosition: MessagePosition) (meter: Meter) : Meter =
        { meter with LastProcessedMessage = messagePosition }

    let toStr (pid: string) (m: Meter) =
        let mStr =
            m.Subscription.Plan.BillingDimensions
            |> Map.toSeq
            |> Seq.map (fun (k,v) -> v)
            |> Seq.map (sprintf "%A")
            |> Seq.map (fun v -> $"{pid} {m.Subscription.MarketplaceResourceId.ToString()}: {v}")
            |> String.concat "\n"

        let uStr =
            m.UsageToBeReported
            |> Seq.map (fun a -> a.ToString())
            |> String.concat "\n"

        $"{mStr}\n{uStr}"