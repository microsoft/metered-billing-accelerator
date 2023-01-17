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

    let usagesToBeReported (meterCollection: MeterCollection) : MarketplaceRequest list =
        if meterCollection.Meters |> Seq.isEmpty
        then []
        else
            meterCollection.Meters
            |> Seq.map (fun x -> x.UsageToBeReported)
            |> Seq.concat
            |> List.ofSeq

    /// Ensure that we apply the right event at the right time, belonging to the appropriate partition
    let private enforceStrictSequenceNumbers ({ SequenceNumber = concreteSequenceNumber; PartitionID = pidEvent }: MessagePosition) (state: MeterCollection) : MeterCollection =
        match state.LastUpdate with
        | Some { SequenceNumber = lastUpdateSequenceNumber; PartitionID = pidState } ->
            //if pidState <> pidEvent
            //then failwith $"Seems your are reading the wrong event stream. The state belongs to partition {pidState.value}, but the event belongs to {pidEvent.value}"
            //else ()

            if lastUpdateSequenceNumber + 1L <> concreteSequenceNumber
            then failwith $"Seems you are reading the wrong event stream. The last state update was sequence number {lastUpdateSequenceNumber}, therefore we expect event with sequence number {lastUpdateSequenceNumber + 1L}, but got {concreteSequenceNumber}"
            else ()
        | None -> ()

        state

    let private handleUnprocessableMessage (m: MeteringUpdateEvent) (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        let newHead =
            { MessagePosition = messagePosition
              EventData = m
              EventsToCatchup = None
              Source = EventHub }

        { state with UnprocessableMessages = newHead :: state.UnprocessableMessages }

    let private handleRemoveUnprocessedMessages { Selection = selection } state =
        match state.LastUpdate with
        | None -> state
        | Some _ ->
            let filter = function
                | BeforeIncluding x -> List.filter (fun e -> e.MessagePosition.SequenceNumber > x) // Keep all with a sequence number greater x in state
                | Exactly x -> List.filter (fun e -> e.MessagePosition.SequenceNumber <> x) // Keep all except x in state

            { state with UnprocessableMessages = state.UnprocessableMessages |> filter selection }

    let private mapIf predicate update a =
        if predicate a
        then update a
        else a

    let private updateIf predicate update =
        List.map (mapIf predicate update)

    /// Updates the meter which matches the given MarketplaceResourceId using the `update` function
    let private updateMeterForMarketplaceResourceId (marketplaceResourceId: MarketplaceResourceId) (update: Meter -> Meter) (meters: Meter list) : Meter list =
        updateIf (Meter.matches marketplaceResourceId) update meters

    let private applyMetersInMeterCollection (handler: Meter list -> Meter list) (state: MeterCollection) : MeterCollection =
        state.Meters
        |> handler
        |> (fun x -> { state with Meters = x })

    let private handleUsageReport (internalUsageEvent: InternalUsageEvent) (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        let marketplaceResourceId = internalUsageEvent.MarketplaceResourceId
        let existingSubscription = state.Meters |> List.exists (Meter.matches marketplaceResourceId)
        if existingSubscription
        then
            let newMeterCollection =
                state.Meters
                |> updateMeterForMarketplaceResourceId marketplaceResourceId
                        (Meter.handleUsageEvent (internalUsageEvent, messagePosition))

            { state with Meters = newMeterCollection }
        else
            state |> handleUnprocessableMessage (UsageReported internalUsageEvent) messagePosition

    let private handleAddedSubscription (subscriptionCreationInformation: SubscriptionCreationInformation) messagePosition (state: MeterCollection) : MeterCollection =
        let addNewSubscriptionIfNonExistent (key: MarketplaceResourceId) (meter: Meter) (existingMeters: Meter list) : Meter list =
            let addOnlyIfNotExists (marketplaceResourceId: MarketplaceResourceId) (meter: Meter) (existingMeters: Meter list) : Meter list =
                if existingMeters |> List.exists (Meter.matches marketplaceResourceId)
                then existingMeters
                else meter :: existingMeters

            addOnlyIfNotExists key meter existingMeters

        let newMeter: Meter = Meter.createNewSubscription subscriptionCreationInformation messagePosition
        let updateMeters : Meter list -> Meter list = addNewSubscriptionIfNonExistent subscriptionCreationInformation.Subscription.MarketplaceResourceId newMeter

        state
        |> applyMetersInMeterCollection updateMeters

    /// Removes a Meter with the given MarketplaceResourceId from the MeterCollection.
    let private handleDeletedSubscription (marketplaceResourceId: MarketplaceResourceId) (state: MeterCollection) : MeterCollection =
        let metersWithoutTheOne =  state.Meters |> List.filter (fun meter -> not (meter |> Meter.matches marketplaceResourceId))
        { state with Meters = metersWithoutTheOne }


    let private handleMarketplaceResponseReceived (submission: MarketplaceResponse) (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        // TODO: Here I must check that there is a matching MarketplaceRequest that can be removed, before applying the compensating money.
        let marketplaceResourceId = MarketplaceSubmissionResult.marketplaceResourceId submission.Result

        let updateSingleMeter: Meter -> Meter = Meter.handleUsageSubmissionToAPI submission messagePosition
        let updateList: Meter list -> Meter list = updateMeterForMarketplaceResourceId marketplaceResourceId updateSingleMeter

        state
        |> applyMetersInMeterCollection updateList

    /// Iterate over all current meters, and check if one of the overages can be converted into a metering API event.
    let handleMeteringTimestamp (now: MeteringDateTime) (state: MeterCollection) : MeterCollection =
        state.Meters
        |> List.map (fun meter -> Meter.closePreviousHourIfNeeded now meter)
        |> (fun updatedMeters -> { state with Meters = updatedMeters })

    let private setLastProcessed (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        { state with LastUpdate = Some messagePosition }

    let handleMeteringEvent (state: MeterCollection) ({EventData = meteringUpdateEvent; MessagePosition = messagePosition; EventsToCatchup = catchup}: EventHubEvent<MeteringUpdateEvent>) : MeterCollection =
        state
        |> enforceStrictSequenceNumbers messagePosition  // This line throws an exception if we're not being fed the right event #
        |> match meteringUpdateEvent with
           | SubscriptionPurchased subscriptionCreationInformation -> handleAddedSubscription subscriptionCreationInformation messagePosition
           | SubscriptionDeletion marketplaceResourceId -> handleDeletedSubscription marketplaceResourceId
           | UsageSubmittedToAPI marketplaceResponse -> handleMarketplaceResponseReceived marketplaceResponse messagePosition
           | UsageReported internalUsageEvent -> handleUsageReport internalUsageEvent messagePosition
           | UnprocessableMessage upm -> handleUnprocessableMessage (UnprocessableMessage upm) messagePosition
           | RemoveUnprocessedMessages rupm -> handleRemoveUnprocessedMessages rupm
           | Ping _ -> id
        |> handleMeteringTimestamp messagePosition.PartitionTimestamp
        |> setLastProcessed messagePosition

    let handleMeteringEvents (meterCollection: MeterCollection option) (meteringEvents: EventHubEvent<MeteringUpdateEvent> list) : MeterCollection =
        let meterCollection = meterCollection |> Option.defaultWith (fun () -> MeterCollection.Empty)

        meteringEvents
        |> List.fold handleMeteringEvent meterCollection
