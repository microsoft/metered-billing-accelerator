// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open NodaTime
open Metering.BaseTypes.EventHub

type Meter =
    { Subscription: Subscription // The purchase information of the subscription
      UsageToBeReported: MarketplaceRequest list // a list of usage elements which haven't yet been reported to the metering API
      LastProcessedMessage: MessagePosition } // Last message which has been applied to this Meter

module Meter =
    let matches (marketplaceResourceId: MarketplaceResourceId) (this: Meter) : bool =
        this.Subscription.MarketplaceResourceId.Matches(marketplaceResourceId)

    let updateDimensions (billingDimensions: BillingDimensions) (meter: Meter) : Meter =
        { meter with Subscription = (meter.Subscription.updateBillingDimensions billingDimensions) }

    //let applyToCurrentMeterValue (f: CurrentMeterValues -> CurrentMeterValues) (this: Meter) : Meter = { this with CurrentMeterValues = (f this.CurrentMeterValues) }
    let setLastProcessedMessage x this = { this with LastProcessedMessage = x }
    let addUsageToBeReported (item: MarketplaceRequest) this = { this with UsageToBeReported = (item :: this.UsageToBeReported) }
    let addUsagesToBeReported (items: MarketplaceRequest list) this = { this with UsageToBeReported = List.concat [ items; this.UsageToBeReported ] }
    /// Removes the item from the UsageToBeReported collection
    let removeUsageToBeReported (item: MarketplaceRequest) this = { this with UsageToBeReported = (this.UsageToBeReported |> List.filter (fun e -> e <> item)) }

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

    let updateConsumptionForApplicationInternalMeterName (quantity: Quantity) (timestamp: MeteringDateTime) (applicationInternalMeterName: ApplicationInternalMeterName) (meter: Meter) : Meter =
        if not quantity.isAllowedIncomingQuantity
        then meter // If the incoming value is not a real (non-negative) number, don't change anything.
        else
            let update : (BillingDimension -> BillingDimension) =
                MeterValue.applyConsumption timestamp quantity

            let updatedDimensions =
                meter.Subscription.Plan.BillingDimensions
                |> Map.change applicationInternalMeterName (Option.map update)

            meter
            |> updateDimensions updatedDimensions

    let previousBillingIntervalCanBeClosedNewEvent (previous: MeteringDateTime) (eventTime: MeteringDateTime) : bool =
        previous.Hour <> eventTime.Hour || eventTime - previous >= Duration.FromHours(1.0)

    let closeAndDebit (quantity: Quantity) (messagePosition: MessagePosition) (applicationInternalMeterName: ApplicationInternalMeterName) (meter: Meter) : Meter =
        let closePreviousIntervalIfNeeded : (Meter -> Meter) =
            let last = meter.LastProcessedMessage.PartitionTimestamp
            let curr = messagePosition.PartitionTimestamp
            if previousBillingIntervalCanBeClosedNewEvent last curr
            then closePreviousMeteringPeriod
            else id

        meter
        |> closePreviousIntervalIfNeeded
        |> updateConsumptionForApplicationInternalMeterName quantity messagePosition.PartitionTimestamp applicationInternalMeterName
        |> setLastProcessedMessage messagePosition

    let handleUsageEvent ((event: InternalUsageEvent), (currentPosition: MessagePosition)) (meter : Meter) : Meter =
        closeAndDebit event.Quantity currentPosition event.MeterName meter

    let closePreviousHourIfNeeded (partitionTimestamp: MeteringDateTime) (meter: Meter) : Meter =
        let previousTimestamp = meter.LastProcessedMessage.PartitionTimestamp

        if previousBillingIntervalCanBeClosedNewEvent previousTimestamp partitionTimestamp
        then meter |> closePreviousMeteringPeriod
        else meter

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
            let theRightDimension (_, billingDimension: BillingDimension) : bool =
                billingDimension |> BillingDimension.hasDimensionId expired.RequestData.DimensionId

            let nameAndDimension =
                meter.Subscription.Plan.BillingDimensions
                |> Map.toSeq
                |> Seq.tryFind theRightDimension

            match nameAndDimension with
            | None -> meter // It seems the dimension in question isn't part of the plan, that is impossible
            | Some (applicationInternalMeterName, _billingDimension) -> meter |> closeAndDebit expired.RequestData.Quantity messagePosition applicationInternalMeterName
        | Generic _ ->
            meter

    let handleUsageSubmissionToAPI (item: MarketplaceResponse) (messagePosition: MessagePosition) (meter: Meter) : Meter =
        match item.Result with
        | Ok success ->  meter |> removeUsageToBeReported success.RequestData
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
          UsageToBeReported = List.empty }
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