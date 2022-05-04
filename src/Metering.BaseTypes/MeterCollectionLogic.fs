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
    let getEventPosition (someMeters: MeterCollection option) : StartingPosition =
        match someMeters with
        | None -> StartingPosition.Earliest
        | Some meters -> meters.LastUpdate |> StartingPosition.from

    [<Extension>]
    let getLastUpdateAsString (meters: MeterCollection) : string =
        match meters.LastUpdate with
        | None -> "Earliest"
        | Some p -> $"partition {p.PartitionID.value} / sequence# {p.SequenceNumber}"

    [<Extension>]
    let getLastSequenceNumber (meters: MeterCollection) : SequenceNumber =
        match meters.LastUpdate with
        | None -> raise (new System.NotSupportedException())
        | Some p -> p.SequenceNumber

    let usagesToBeReported (meters: MeterCollection) : MarketplaceRequest list =
        if meters.MeterCollection |> Seq.isEmpty 
        then []
        else
            meters.MeterCollection
            |> Seq.map (fun x -> x.Value.UsageToBeReported)
            |> Seq.concat
            |> List.ofSeq
    
    let private addOnlyIfNotExists<'Key,'T when 'Key: comparison> (key: 'Key) (value: 'T) (table: Map<'Key,'T>) : Map<'Key,'T> =
        if Map.containsKey key table
        then table
        else Map.add key value table

    let private handleSubscriptionPurchased<'Key,'T when 'Key: comparison> (key: 'Key) (value: 'T) (table: Map<'Key,'T>) : Map<'Key,'T> =
        let ignoreAdditionalSubscriptionMessages = true

        let handle = 
            if ignoreAdditionalSubscriptionMessages 
            then addOnlyIfNotExists 
            else Map.add

        handle key value table

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

    let private addUsage (internalUsageEvent: InternalUsageEvent) messagePosition state =
        let existingSubscription = state.MeterCollection |> Map.containsKey internalUsageEvent.InternalResourceId 
        if existingSubscription
        then 
            let newMeterCollection =
                state.MeterCollection
                |> Map.change 
                    internalUsageEvent.InternalResourceId 
                    (Option.bind ((Meter.handleUsageEvent (internalUsageEvent, messagePosition)) >> Some))

            { state with MeterCollection = newMeterCollection }
        else
            state |> addUnprocessableMessage (UsageReported internalUsageEvent) messagePosition

    let private applyMeters (handler: Map<InternalResourceId, Meter> -> Map<InternalResourceId, Meter>) (state: MeterCollection)  : MeterCollection =
        let newMeterCollection = state.MeterCollection |> handler
        { state with MeterCollection = newMeterCollection } 

    let private addSubscription (subscriptionCreationInformation: SubscriptionCreationInformation) messagePosition state =
        let meter = Meter.createNewSubscription subscriptionCreationInformation messagePosition

        state 
        |> applyMeters (handleSubscriptionPurchased subscriptionCreationInformation.Subscription.InternalResourceId meter)

    let private deleteSubscription internalResourceId state = 
         { state with MeterCollection = state.MeterCollection |> Map.remove internalResourceId }

    let private usageSubmitted submission messagePosition state =
        state
        //|> applyMeters (Map.change submission.Payload.ResourceId (Option.map (Meter.handleUsageSubmissionToAPI config submission)))
        |> applyMeters (Map.change (submission.Result |> MarketplaceSubmissionResult.resourceId) (Option.bind ((Meter.handleUsageSubmissionToAPI submission messagePosition) >> Some)))

    /// Iterate over all current meters, and check if one of the overages can be converted into a metering API event.
    let handleMeteringTimestamp now state =
        state.MeterCollection
        |> Map.map (fun _ meter -> Meter.closePreviousHourIfNeeded now meter)
        |> (fun updatedMeters -> { state with MeterCollection = updatedMeters })

    let private setLastProcessed (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        { state with LastUpdate = Some messagePosition }

    let handleMeteringEvent (state: MeterCollection) ({EventData = meteringUpdateEvent; MessagePosition = messagePosition; EventsToCatchup = catchup}: EventHubEvent<MeteringUpdateEvent>) : MeterCollection =
        state
        |> enforceStrictSequenceNumbers messagePosition  // This line throws an exception if we're not being fed the right event #
        |> match meteringUpdateEvent with
           | SubscriptionPurchased subscriptionCreationInformation -> addSubscription subscriptionCreationInformation messagePosition
           | SubscriptionDeletion internalResourceId -> deleteSubscription internalResourceId
           | UsageSubmittedToAPI marketplaceResponse -> usageSubmitted marketplaceResponse messagePosition            
           | UsageReported internalUsageEvent -> addUsage internalUsageEvent messagePosition
           | UnprocessableMessage upm -> addUnprocessableMessage (UnprocessableMessage upm) messagePosition
           | RemoveUnprocessedMessages rupm -> removeUnprocessedMessages rupm
        |> handleMeteringTimestamp messagePosition.PartitionTimestamp
        |> setLastProcessed messagePosition 

    let handleMeteringEvents (meterCollection: MeterCollection option) (meteringEvents: EventHubEvent<MeteringUpdateEvent> list) : MeterCollection =
        let meterCollection =
            match meterCollection with
            | None -> MeterCollection.Empty
            | Some meterCollection -> meterCollection

        meteringEvents
        |> List.fold  handleMeteringEvent meterCollection
