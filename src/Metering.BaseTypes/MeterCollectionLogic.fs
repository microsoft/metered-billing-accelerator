// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open System.Runtime.CompilerServices
open Metering.BaseTypes.EventHub

[<Extension>]
module MeterCollectionLogic =
    open MeterCollection

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
    
    let private addOnlyIfNotExists (marketplaceResourceId: MarketplaceResourceId) (value: Meter) (table: Meter list) : Meter list =
        if table |> List.exists (Meter.matches marketplaceResourceId)
        then table
        else value :: table

    let private handleSubscriptionPurchased (key: MarketplaceResourceId) (value: Meter) (table: Meter list) : Meter list =
        addOnlyIfNotExists key value table

    /// Ensure that we apply the right event at the right time, belonging to the appropriate partition
    let private enforceStrictSequenceNumbers ({ SequenceNumber = concreteSequenceNumber; PartitionID = pidEvent }: MessagePosition)  (state: MeterCollection) : MeterCollection =
        match state.LastUpdate with 
        | Some { SequenceNumber = lastUpdateSequenceNumber; PartitionID = pidState } -> 
            //if pidState <> pidEvent
            //then failwith $"Seems your are reading the wrong event stream. The state belongs to partition {pidState |> PartitionID.value}, but the event belongs to {pidEvent |> PartitionID.value}"
            //else ()

            if lastUpdateSequenceNumber + 1L <> concreteSequenceNumber 
            then failwith $"Seems you are reading the wrong event stream. The last state update was sequence number {lastUpdateSequenceNumber}, therefore we expect event with sequence number {lastUpdateSequenceNumber + 1L}, but got {concreteSequenceNumber}" 
            else ()
        | None -> ()

        state

    let private addUnprocessableMessage (m: MeteringUpdateEvent) (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        let newHead = 
            { MessagePosition = messagePosition
              EventData = m
              EventsToCatchup = None
              Source = EventHub }

        { state with UnprocessableMessages = newHead :: state.UnprocessableMessages }
            
    let private removeUnprocessedMessages { Selection = selection } state =
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

    let private updateMeter (marketplaceResourceId: MarketplaceResourceId) (update: Meter -> Meter) (meters: Meter list) : Meter list = 
        updateIf (Meter.matches marketplaceResourceId) update meters

    let private applyMeters (handler: Meter list -> Meter list) (state: MeterCollection)  : MeterCollection =
        state.Meters
        |> handler
        |> (fun x -> { state with Meters = x })

    let private addUsage (internalUsageEvent: InternalUsageEvent) (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        let marketplaceResourceId = internalUsageEvent.MarketplaceResourceId
        let existingSubscription = state.Meters |> List.exists (Meter.matches marketplaceResourceId)
        if existingSubscription
        then 
            let newMeterCollection =
                state.Meters
                |> updateMeter 
                    marketplaceResourceId 
                    (Meter.handleUsageEvent (internalUsageEvent, messagePosition))

            { state with Meters = newMeterCollection }
        else
            state |> addUnprocessableMessage (UsageReported internalUsageEvent) messagePosition

    let private addSubscription (subscriptionCreationInformation: SubscriptionCreationInformation) messagePosition state =
        let meter: Meter = Meter.createNewSubscription subscriptionCreationInformation messagePosition
        let updateList: Meter list -> Meter list = handleSubscriptionPurchased subscriptionCreationInformation.Subscription.MarketplaceResourceId meter

        state 
        |> applyMeters updateList
        
    let private deleteSubscription marketplaceResourceId state = 
        let metersWithoutTheOne =  state.Meters |> List.filter (fun meter -> not (meter |> Meter.matches marketplaceResourceId))
        { state with Meters = metersWithoutTheOne }

    let private usageSubmitted (submission: MarketplaceResponse) (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        let marketplaceResourceId = submission.Result |> MarketplaceSubmissionResult.marketplaceResourceId
        let update: Meter -> Meter = Meter.handleUsageSubmissionToAPI submission messagePosition
        let updateList: Meter list -> Meter list = updateMeter marketplaceResourceId update

        state
        |> applyMeters updateList

    /// Iterate over all current meters, and check if one of the overages can be converted into a metering API event.
    let handleMeteringTimestamp now state =
        state.Meters
        |> List.map (fun meter -> Meter.closePreviousHourIfNeeded now meter)
        |> (fun updatedMeters -> { state with Meters = updatedMeters })

    let private setLastProcessed (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        { state with LastUpdate = Some messagePosition }

    let handleMeteringEvent (state: MeterCollection) ({EventData = meteringUpdateEvent; MessagePosition = messagePosition; EventsToCatchup = catchup}: EventHubEvent<MeteringUpdateEvent>) : MeterCollection =
        state
        |> enforceStrictSequenceNumbers messagePosition  // This line throws an exception if we're not being fed the right event #
        |> match meteringUpdateEvent with
           | SubscriptionPurchased subscriptionCreationInformation -> addSubscription subscriptionCreationInformation messagePosition
           | SubscriptionDeletion marketplaceResourceId -> deleteSubscription marketplaceResourceId
           | UsageSubmittedToAPI marketplaceResponse -> usageSubmitted marketplaceResponse messagePosition            
           | UsageReported internalUsageEvent -> addUsage internalUsageEvent messagePosition
           | UnprocessableMessage upm -> addUnprocessableMessage (UnprocessableMessage upm) messagePosition
           | RemoveUnprocessedMessages rupm -> removeUnprocessedMessages rupm
        |> handleMeteringTimestamp messagePosition.PartitionTimestamp
        |> setLastProcessed messagePosition 

    let handleMeteringEvents (meterCollection: MeterCollection option) (meteringEvents: EventHubEvent<MeteringUpdateEvent> list) : MeterCollection =
        let meterCollection = meterCollection |> Option.defaultWith (fun () -> MeterCollection.Empty)

        meteringEvents
        |> List.fold handleMeteringEvent meterCollection
