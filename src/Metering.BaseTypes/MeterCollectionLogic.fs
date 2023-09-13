// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open System.Runtime.CompilerServices
open Metering.BaseTypes.EventHub

[<Extension>]
module MeterCollectionLogic =
    let lastUpdate (mc: MeterCollection option) : MessagePosition option =
        mc |> Option.bind (fun m -> m.LastUpdate)

    [<Extension>]
    let getEventPosition (someMeterCollection: MeterCollection option) : StartingPosition =
        match someMeterCollection with
        | None -> StartingPosition.Earliest
        | Some meters -> meters.LastUpdate |> StartingPosition.from

    [<Extension>]
    let getLastUpdateAsString (meterCollection: MeterCollection) : string =
        match meterCollection.LastUpdate with
        | None -> "Earliest"
        | Some p -> $"partition {p.PartitionID.value} / sequence# {p.SequenceNumber}"

    [<Extension>]
    let getLastSequenceNumber (meterCollection: MeterCollection) : SequenceNumber =
        match meterCollection.LastUpdate with
        | None -> raise (new System.NotSupportedException())
        | Some p -> p.SequenceNumber

    /// Returns all MarketplaceRequests that are ready to be sent to the Marketplace Metering API
    let usagesToBeReported (meterCollection: MeterCollection) : MarketplaceRequest list =
        meterCollection.Meters
        |> List.map (fun x -> x.UsageToBeReported)
        |> List.concat

    /// Ensure that we apply the right event at the right time, belonging to the appropriate partition
    let private enforceStrictSequenceNumbers (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        let { SequenceNumber = concreteSequenceNumber; PartitionID = partitionIdIncomingEvent } = messagePosition
        match state.LastUpdate with
        | Some { SequenceNumber = lastUpdateSequenceNumber; PartitionID = partitionIdStateFile } ->
            if partitionIdStateFile <> partitionIdIncomingEvent
            then failwith $"Seems your are reading the wrong event stream. The state belongs to partition {partitionIdStateFile.value}, but the event belongs to {partitionIdIncomingEvent.value}."
            else
                if lastUpdateSequenceNumber + 1L <> concreteSequenceNumber
                then failwith $"Seems you are reading the wrong event stream. The last state update was sequence number {lastUpdateSequenceNumber}, therefore we expect event with sequence number {lastUpdateSequenceNumber + 1L}, but got {concreteSequenceNumber}."
                else ()
        | None -> ()

        state

    /// Adds an unprocessable message to the state.
    let private handleUnprocessableMessage (m: UnprocessableMessage) (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        let newHead =
            { MessagePosition = messagePosition
              EventData = UnprocessableMessage m
              EventsToCatchup = None
              Source = EventHub }

        { state with UnprocessableMessages = newHead :: state.UnprocessableMessages }

    /// Removes one or more unprocessable messages from the state.
    let private handleRemoveUnprocessedMessages (removeUnprocessedMessages: RemoveUnprocessedMessages) (state: MeterCollection) : MeterCollection =
        let selection = removeUnprocessedMessages.Selection

        match state.LastUpdate with
        | None -> state
        | Some _ ->
            let filter = function
                | BeforeIncluding x -> List.filter (fun e -> e.MessagePosition.SequenceNumber > x) // Keep all with a sequence number greater x in state
                | Exactly x -> List.filter (fun e -> e.MessagePosition.SequenceNumber <> x) // Keep all except x in state

            { state with UnprocessableMessages = state.UnprocessableMessages |> filter selection }

    let private listMapIf (predicate: 'T -> bool) (mapping: 'T -> 'T) : ('T list -> 'T list) =
         List.map (fun t -> if predicate t then mapping t else t)


    let private listMapIf2 (finalizer: 'T -> 'T) (mappingNonMatch: 'T -> 'T) (predicate: 'T -> bool) (mappingMatch: 'T -> 'T) (l: 'T list) : ('T list * bool) =
        // Iterate over the collection.
        // If the predicate matches an entry, apply the mappingMatch function (and later indicate we found a matching entry)
        // If the predicate does not match, apply the mappingNonMatch function
        // If the entry was updated (by the mappingMatch or mappingNonMatch functions), apply the finalizer function
        let mutable found = false

        let result = l |> List.map (fun t ->
            let updated =
                if predicate t
                then
                    found <- true
                    t |> mappingMatch
                else
                    t |> mappingNonMatch

            if updated <> t
            then updated |> finalizer
            else updated
        )

        result, found

    //let private listMapMetersIf (messagePosition: MessagePosition) predicate mappingMatch =
    //    let mappingMatch =
    //        mappingMatch
    //        >> Meter.closeHour messagePosition.PartitionTimestamp
    //    let mappingNonMatch =
    //        Meter.closePreviousHourIfNeeded messagePosition.PartitionTimestamp
    //        >> Meter.resetCountersIfNewBillingCycleStarted messagePosition
    //    let finalizer =
    //        Meter.setLastProcessedMessage messagePosition

    //    listMapIf2
    //        finalizer
    //        mappingNonMatch
    //        predicate
    //        mappingMatch

    let private experiment
        (marketplaceResourceId: MarketplaceResourceId)
        (messagePosition: MessagePosition)
        (updateExistingMeter: MessagePosition -> Meter -> Meter)
        (state: MeterCollection)
        : MeterCollection =

        let matchingTheMeter = Meter.matches marketplaceResourceId
        let updateMeter = updateExistingMeter messagePosition
        let setLastProcessedMessage = Meter.setLastProcessedMessage messagePosition
        let update = updateMeter >> setLastProcessedMessage

        let updatedMeters = state.Meters |> listMapIf matchingTheMeter update

        { state with
            Meters = updatedMeters
            LastUpdate = Some messagePosition }

    let private handleSubscriptionPurchased (subscriptionCreationInformation: SubscriptionCreationInformation) (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        let matches = Meter.matches subscriptionCreationInformation.Subscription.MarketplaceResourceId
        if state.Meters |> List.exists matches
        then state
        else
            let meter = Meter.createNewSubscription subscriptionCreationInformation messagePosition
            { state with Meters = meter :: state.Meters }

    /// Remove all meters that are requested to be deleted, and have no usage still to be reported.
    let private deleteAllDeletableSubscriptions (state: MeterCollection) : MeterCollection =
        /// Meter is flagged for deletion and has no usage to be reported
        let meterCannotBeDeleted meter = not meter.DeletionRequested || not meter.UsageToBeReported.IsEmpty
        { state with Meters = state.Meters |> List.filter meterCannotBeDeleted }

    /// Flags a Meter for deletion.
    let private handleSubscriptionDeletion (marketplaceResourceId: MarketplaceResourceId) (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        // When the deletion request event comes in, we first flag the subscription for deletion, and we move all non-billed consumption
        // **immediately** into the to-be-reported collection. Once these values are successfully submitted, we remove the subscription from the collection.
        let now = messagePosition.PartitionTimestamp

        let matchingTheMeterPredicate = Meter.matches marketplaceResourceId

        let mappingMatch meter =
            // update first transfers all non-billed consumption into the to-be-reported collection, and then flags the meter for deletion.
            meter
            |> Meter.``closeHour!`` now
            |> (fun m -> { m with DeletionRequested = true })

        let mappingNonMatch =
            Meter.touchMeter messagePosition

        let finalizer =
            Meter.setLastProcessedMessage messagePosition

        let updatedMeters, _ =
            state.Meters
            |> listMapIf2 finalizer mappingNonMatch matchingTheMeterPredicate mappingMatch

        { state with
            Meters = updatedMeters
            LastUpdate = Some messagePosition }

    let private handleUsageReported (internalUsageEvent: InternalUsageEvent) (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        let existsAndIsntFlaggedForDeletionPredicate =
            Meter.matchesAndNotDeletionRequested internalUsageEvent.MarketplaceResourceId

        let mappingMatch =
            //Meter.touchMeter messagePosition
            //>>
            Meter.handleUsageEvent (internalUsageEvent, messagePosition)

        let mappingNonMatch =
            Meter.touchMeter messagePosition

        let finalizer =
            Meter.setLastProcessedMessage messagePosition

        let updatedMeters, foundAMatchingAndActiveMeter =
            state.Meters
            |> listMapIf2 finalizer mappingNonMatch existsAndIsntFlaggedForDeletionPredicate mappingMatch

        if foundAMatchingAndActiveMeter
        then { state with Meters = updatedMeters }
        else state |> handleUnprocessableMessage (UnprocessableUsageEvent internalUsageEvent) messagePosition

    let private handleUsageSubmittedToAPI (marketplaceResponse: MarketplaceResponse) (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        // TODO: Here I must check that there is a matching MarketplaceRequest that can be removed, before applying the compensating money.

        let resourceId =
            MarketplaceSubmissionResult.marketplaceResourceId marketplaceResponse.Result

        let existsAndIsntFlaggedForDeletionPredicate =
            Meter.matchesAndNotDeletionRequested resourceId

        let mappingMatch =
            Meter.touchMeter messagePosition
            >> Meter.handleUsageSubmissionToAPI marketplaceResponse messagePosition

        let mappingNonMatch =
            Meter.touchMeter messagePosition

        let finalizer =
            Meter.setLastProcessedMessage messagePosition

        let updatedMeters, _foundAMatchingAndActiveMeter =
            state.Meters
            |> listMapIf2 finalizer mappingNonMatch existsAndIsntFlaggedForDeletionPredicate mappingMatch

        { state with Meters = updatedMeters }

    /// Iterate over all current meters, and check if one of the overages can be converted into a metering API event.
    let handleMeteringTimestamp (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        let noMatchPredicate = (fun _ -> false)

        let mappingMatch = id

        let mappingNonMatch =
            Meter.touchMeter messagePosition

        let finalizer =
            Meter.setLastProcessedMessage messagePosition

        let updatedMeters, _ = listMapIf2 finalizer mappingNonMatch noMatchPredicate mappingMatch state.Meters

        { state with
            Meters = updatedMeters
            LastUpdate = Some messagePosition }

    let handleSubscriptionUpdated (update: SubscriptionUpdate) (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        raise (new System.NotImplementedException("SubscriptionUpdated is not implemented yet"))

    let handleMeteringEvent (state: MeterCollection) ({EventData = meteringUpdateEvent; MessagePosition = messagePosition; EventsToCatchup = catchup}: EventHubEvent<MeteringUpdateEvent>) : MeterCollection =
        state
        |> enforceStrictSequenceNumbers messagePosition  // This line throws an exception if we're not being fed the right event #
        |> match meteringUpdateEvent with
           | SubscriptionPurchased request ->      handleSubscriptionPurchased     request messagePosition
           | SubscriptionUpdated request ->        handleSubscriptionUpdated       request messagePosition
           | SubscriptionDeletion request ->       handleSubscriptionDeletion      request messagePosition
           | UsageReported request ->              handleUsageReported             request messagePosition
           | UsageSubmittedToAPI request ->        handleUsageSubmittedToAPI       request messagePosition
           | UnprocessableMessage request ->       handleUnprocessableMessage      request messagePosition
           | RemoveUnprocessedMessages request ->  handleRemoveUnprocessedMessages request
           | Ping _ ->                             id
        |> handleMeteringTimestamp messagePosition
        |> deleteAllDeletableSubscriptions

    let handleMeteringEvents (meterCollection: MeterCollection option) (meteringEvents: EventHubEvent<MeteringUpdateEvent> list) : MeterCollection =
        let meterCollection = meterCollection |> Option.defaultWith (fun () -> MeterCollection.Empty)

        meteringEvents
        |> List.fold handleMeteringEvent meterCollection
