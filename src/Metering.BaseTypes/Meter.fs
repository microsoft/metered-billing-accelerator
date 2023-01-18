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

    let updateDimensions (billingDimensions: BillingDimensions) (meter: Meter) : Meter =
        { meter with Subscription = (meter.Subscription.updateBillingDimensions billingDimensions) }

    //let applyToCurrentMeterValue (f: CurrentMeterValues -> CurrentMeterValues) (this: Meter) : Meter = { this with CurrentMeterValues = (f this.CurrentMeterValues) }
    let setLastProcessedMessage x this = { this with LastProcessedMessage = x }
    let addUsageToBeReported (item: MarketplaceRequest) this = { this with UsageToBeReported = (item :: this.UsageToBeReported) }
    let addUsagesToBeReported (items: MarketplaceRequest list) this = { this with UsageToBeReported = List.concat [ items; this.UsageToBeReported ] }

    /// This function must be called when 'a new hour' started, i.e. the previous period must be closed.
    let closePreviousMeteringPeriod (state: Meter) : Meter =
        let closeHour = MeterValue.closeHour state.Subscription.MarketplaceResourceId state.Subscription.Plan.PlanId

        let results : (ApplicationInternalMeterName * (MarketplaceRequest list * BillingDimension)) seq =
            state.Subscription.Plan.BillingDimensions
            |> Map.toSeq
            |> Seq.map (fun (name, billingDimension) -> (name, closeHour name billingDimension))

        let updatedBillingDimensions =
            results
            |> Seq.map (fun (name,(_marketplaceRequests, newMeter)) -> (name, newMeter))
            |> Map.ofSeq

        let usagesToBeReported =
            results
            |> Seq.map (fun (_name, (marketplaceRequests, _newMeter)) -> marketplaceRequests)
            |> List.concat

        state
        |> updateDimensions updatedBillingDimensions
        |> addUsagesToBeReported usagesToBeReported

    let updateConsumptionForApplicationInternalMeterName (quantity: Quantity) (timestamp: MeteringDateTime) (applicationInternalMeterName: ApplicationInternalMeterName) (mechanism: MeteringDateTime -> Quantity -> BillingDimension -> BillingDimension) (meter: Meter) : Meter =
        if not quantity.isAllowedIncomingQuantity
        then meter // If the incoming value is not a real (non-negative) number, don't change anything.
        else
            let update : (BillingDimension -> BillingDimension) = mechanism timestamp quantity

            let updatedDimensions =
                meter.Subscription.Plan.BillingDimensions
                |> Map.change applicationInternalMeterName (Option.map update)

            meter
            |> updateDimensions updatedDimensions

    let previousBillingIntervalCanBeClosedNewEvent (previous: MeteringDateTime) (eventTime: MeteringDateTime) : bool =
        previous.Hour <> eventTime.Hour || eventTime - previous >= Duration.FromHours(1.0)

    let closeAndDebit (quantity: Quantity) (messagePosition: MessagePosition) (applicationInternalMeterName: ApplicationInternalMeterName) (mechanism: MeteringDateTime -> Quantity -> BillingDimension -> BillingDimension) (meter: Meter) : Meter =
        let closePreviousIntervalIfNeeded : (Meter -> Meter) =
            let last = meter.LastProcessedMessage.PartitionTimestamp
            let curr = messagePosition.PartitionTimestamp
            if previousBillingIntervalCanBeClosedNewEvent last curr
            then closePreviousMeteringPeriod
            else id

        meter
        |> closePreviousIntervalIfNeeded
        |> updateConsumptionForApplicationInternalMeterName quantity messagePosition.PartitionTimestamp applicationInternalMeterName mechanism
        |> setLastProcessedMessage messagePosition

    let handleUsageEvent ((event: InternalUsageEvent), (currentPosition: MessagePosition)) (meter : Meter) : Meter =
        closeAndDebit event.Quantity currentPosition event.MeterName MeterValue.applyConsumption meter

    let closePreviousHourIfNeeded (partitionTimestamp: MeteringDateTime) (meter: Meter) : Meter =
        let previousTimestamp = meter.LastProcessedMessage.PartitionTimestamp

        if previousBillingIntervalCanBeClosedNewEvent previousTimestamp partitionTimestamp
        then meter |> closePreviousMeteringPeriod
        else meter

    let private removeUsageForMarketplaceSuccessResponse (successfulResponse: MarketplaceSuccessResponse) (meter: Meter) : Meter =
        let request: MarketplaceRequest= successfulResponse.RequestData
        { meter with UsageToBeReported = meter.UsageToBeReported |> List.filter (fun x -> x <> request )}

    type UsageReportedRemovalResult =
        | UsageReportRemovedFromMeter
        | UsageReportNotFound

    let private removeUsageForMarketplaceSubmissionError (submissionError: MarketplaceSubmissionError) (meter: Meter) : (UsageReportedRemovalResult * Meter) =
        let failedRequest: MarketplaceRequest = MarketplaceSubmissionResult.requestFromError submissionError

        if meter.UsageToBeReported |> List.contains failedRequest
        then (UsageReportRemovedFromMeter, { meter with UsageToBeReported = meter.UsageToBeReported |> List.filter (fun x -> x <> failedRequest )})
        else (UsageReportNotFound, meter)

    let handleUnsuccessfulMeterSubmission (submissionError: MarketplaceSubmissionError) (messagePosition: MessagePosition) (meter: Meter) : Meter =
        match submissionError with
        | DuplicateSubmission _ ->
            let (_status, meter) = removeUsageForMarketplaceSubmissionError submissionError meter
            meter
        | ResourceNotFound _ ->
            let (_status, meter) = removeUsageForMarketplaceSubmissionError submissionError meter
            // TODO When the resource doesn't exist in marketplace, we need to raise some alarm bells here.
            meter
        | Expired expired ->
            // Seems we're trying to submit a report which should have been submitted 24h ago (or even earlier).
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

                let nameAndDimension =
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
                    closeAndDebit expired.RequestData.Quantity messagePosition applicationInternalMeterName applyWithoutUpdatingTotal meter
        | Generic _ ->
            meter

    let handleUsageSubmissionToAPI (item: MarketplaceResponse) (messagePosition: MessagePosition) (meter: Meter) : Meter =
        match item.Result with
        | Ok success ->  meter |> removeUsageForMarketplaceSuccessResponse success
        | Error error -> meter |> handleUnsuccessfulMeterSubmission error messagePosition

    /// Applies the updateBillingDimension function to each BillingDimension
    let applyUpdateToBillingDimensionsInMeter (update: BillingDimension -> BillingDimension) (meter: Meter) : Meter =
        let updatedDimensions = meter.Subscription.Plan.BillingDimensions |> BillingDimensions.update update

        meter
        |> updateDimensions updatedDimensions

    let topupMonthlyCreditsOnNewSubscription (now: MeteringDateTime) (meter: Meter) : Meter =
        let topUp: (BillingDimension -> BillingDimension) = BillingDimension.newBillingCycle now

        meter
        |> applyUpdateToBillingDimensionsInMeter topUp

    let createNewSubscription (subscriptionCreationInformation: SubscriptionCreationInformation) (messagePosition: MessagePosition) : Meter =
        // When we receive the creation of a subscription
        { Subscription = subscriptionCreationInformation.Subscription
          LastProcessedMessage = messagePosition
          UsageToBeReported = List.empty
          DeletionRequested = false }
        |> topupMonthlyCreditsOnNewSubscription messagePosition.PartitionTimestamp

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