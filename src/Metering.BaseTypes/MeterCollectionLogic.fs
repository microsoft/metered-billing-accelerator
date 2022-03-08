// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Types

open System.Runtime.CompilerServices
open NodaTime

[<Extension>]
module MeterCollectionLogic =
    open MeterCollection

    let lastUpdate (someMeterCollection: MeterCollection option) : MessagePosition option = 
        match someMeterCollection with 
        | None -> None
        | Some meters -> meters.LastUpdate 

    [<Extension>]
    let getEventPosition (someMeters: MeterCollection option) : StartingPosition =
        match someMeters with
        | None -> StartingPosition.Earliest
        | Some meters -> meters.LastUpdate |> MessagePosition.startingPosition

    [<Extension>]
    let getLastUpdateAsString (meters: MeterCollection) : string =
        match meters.LastUpdate with
        | None -> "Earliest"
        | Some p -> $"partition {p.PartitionID |> PartitionID.value} / sequence# {p.SequenceNumber}"

    [<Extension>]
    let getLastSequenceNumber (meters: MeterCollection) : SequenceNumber =
        match meters.LastUpdate with
        | None -> raise (new System.NotSupportedException())
        | Some p -> p.SequenceNumber

    let usagesToBeReported (meters: MeterCollection) : MarketplaceRequest list =
        if meters |> value |> Seq.isEmpty 
        then []
        else
            meters
            |> value
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

    let addUnprocessableMessage (usage: EventHubEvent<MeteringUpdateEvent>) (state: MeterCollection) : MeterCollection =
        { state with UnprocessableMessages = usage :: state.UnprocessableMessages }

    let setLastProcessed (messagePosition: MessagePosition) (state: MeterCollection) : MeterCollection =
        { state with LastUpdate = Some messagePosition }
        
    let handleMeteringEvent (timeProvider: CurrentTimeProvider) (gracePeriod: Duration) (state: MeterCollection) (meteringEvent: MeteringEvent) : MeterCollection =    
        // SubscriptionPurchased should add / overwrite existing entry
        // AggregatorBooted should trigger on all entries
        // UsageReported and UsageSubmittedToAPI should fire on the appropriate entry

        let enforceStrictSequenceNumbers (state: MeterCollection) messagePosition =
            match state.LastUpdate with 
            | Some lastUpdate -> 
                let expectedSequenceNumber = lastUpdate.SequenceNumber + 1L
                if expectedSequenceNumber <> messagePosition.SequenceNumber 
                then 
                    failwith 
                        (sprintf "Seems you are missing some events. The last state update was sequence number %d, therefore we expect event %d, but got %d" 
                            lastUpdate.SequenceNumber
                            expectedSequenceNumber
                            messagePosition.SequenceNumber)
                else ()
            | None -> ()

        let applyMeters (handler: Map<InternalResourceId, Meter> -> Map<InternalResourceId, Meter>) (state: MeterCollection)  : MeterCollection =
            let newMeterCollection = state |> value |> handler
            { state with MeterCollection = newMeterCollection }

        match meteringEvent with
        | Remote (Event = meteringUpdateEvent; MessagePosition = messagePosition; EventsToCatchup = catchup) ->
            enforceStrictSequenceNumbers state messagePosition

            match meteringUpdateEvent with
            | SubscriptionPurchased s -> 
                state
                |> applyMeters (handleSubscriptionPurchased s.Subscription.InternalResourceId (Meter.createNewSubscription s messagePosition))
                |> setLastProcessed messagePosition
            | SubscriptionDeletion s ->
                { state with MeterCollection = state.MeterCollection |> Map.remove s }
                |> setLastProcessed messagePosition
            | UsageSubmittedToAPI submission ->
                state
                //|> applyMeters (Map.change submission.Payload.ResourceId (Option.map (Meter.handleUsageSubmissionToAPI config submission)))
                |> applyMeters (Map.change (submission.Result |> MarketplaceSubmissionResult.resourceId) (Option.bind ((Meter.handleUsageSubmissionToAPI submission) >> Some)))
                |> setLastProcessed messagePosition
            | UsageReported usage -> 
                state 
                |> (fun state -> 
                    let existingSubscription = state |> value |> Map.containsKey usage.InternalResourceId 
                
                    if not existingSubscription
                    then 
                        state
                        |> addUnprocessableMessage 
                            { MessagePosition = messagePosition
                              EventData = UsageReported usage 
                              EventsToCatchup = None; Source = EventHub }
                    else
                        let newMeterCollection =
                            state |> value
                            |> Map.change 
                                usage.InternalResourceId 
                                (Option.bind ((Meter.handleUsageEvent (usage, messagePosition)) >> Some))

                        { state with MeterCollection = newMeterCollection }
                )
                |> setLastProcessed messagePosition
            | UnprocessableMessage m -> 
                state
                |> addUnprocessableMessage 
                    { MessagePosition = messagePosition
                      EventData = UnprocessableMessage m 
                      EventsToCatchup = None; Source = EventHub }
                |> setLastProcessed messagePosition
            | RemoveUnprocessedMessages { PartitionID = eventPid; Selection = selection } ->
                match state.LastUpdate with
                | None -> state
                | Some { PartitionID = statePid } -> 
                    if statePid <> eventPid // If the message is targeted to a different partition, don't change the state
                    then state
                    else 
                        let filter = function
                        | BeforeIncluding x -> List.filter (fun e -> e.MessagePosition.SequenceNumber > x) // Keep all with a sequence number greater x in state
                        | Exactly x -> List.filter (fun e -> e.MessagePosition.SequenceNumber <> x) // Keep all except x in state
                                    
                        { state with UnprocessableMessages = state.UnprocessableMessages |> filter selection }
                        |> setLastProcessed messagePosition
        | Local (Event = meteringUpdateEvent) ->
            match meteringUpdateEvent with
            | PartitionEventConsumptionCatchedUp ->
                state
                |> applyMeters (
                    Map.toSeq
                    >> Seq.map(fun (k, v) -> (k, v |> Meter.handleAggregatorCatchedUp timeProvider gracePeriod))
                    >> Map.ofSeq
                )

    let handleMeteringEvents (timeProvider: CurrentTimeProvider) (gracePeriod: Duration) (state: MeterCollection option) (meteringEvents: MeteringEvent list) : MeterCollection =
        let state =
            match state with
            | None -> MeterCollection.Empty
            | Some meterCollection -> meterCollection

        meteringEvents |> List.fold (handleMeteringEvent timeProvider gracePeriod) state
