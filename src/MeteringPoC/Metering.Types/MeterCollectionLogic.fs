namespace Metering.Types

open System.Runtime.CompilerServices
open Azure.Messaging.EventHubs.Consumer
open Metering.Types.EventHub

[<Extension>]
module MeterCollectionLogic =
    open MeterCollection

    let lastUpdate (someMeterCollection: MeterCollection option) : MessagePosition option = 
        match someMeterCollection with 
        | None -> None
        | Some meters -> meters.LastUpdate 

    [<Extension>]
    let getEventPosition (someMeters: MeterCollection option) : EventPosition =
        match someMeters with
        | None -> EventPosition.Earliest
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
        
    let handleMeteringEvent (config: MeteringConfigurationProvider) (state: MeterCollection) (meteringEvent: MeteringEvent) : MeterCollection =    
        // SubscriptionPurchased should add / overwrite existing entry
        // AggregatorBooted should trigger on all entries
        // UsageReported and UsageSubmittedToAPI should fire on the appropriate entry

        let applyMeters (handler: Map<InternalResourceId, Meter> -> Map<InternalResourceId, Meter>) (state: MeterCollection)  : MeterCollection =
            let newMeterCollection = state |> value |> handler
            { state with MeterCollection = newMeterCollection }

        match meteringEvent.MeteringUpdateEvent with
        | SubscriptionPurchased s -> 
            state
            |> applyMeters (handleSubscriptionPurchased s.Subscription.InternalResourceId (Meter.createNewSubscription s meteringEvent.MessagePosition))
            |> setLastProcessed meteringEvent.MessagePosition
        | AggregatorBooted ->
            state
            |> applyMeters (
                Map.toSeq
                >> Seq.map(fun (k, v) -> (k, v |> Meter.handleAggregatorBooted config))
                >> Map.ofSeq
            )
            |> setLastProcessed meteringEvent.MessagePosition
        | UsageSubmittedToAPI submission ->
            state
            //|> applyMeters (Map.change submission.Payload.ResourceId (Option.map (Meter.handleUsageSubmissionToAPI config submission)))
            |> applyMeters (Map.change (submission |> MarketplaceSubmissionResult.resourceId) (Option.bind ((Meter.handleUsageSubmissionToAPI config submission) >> Some)))
            |> setLastProcessed meteringEvent.MessagePosition
        | UsageReported usage -> 
            state 
            |> (fun state -> 
                let existingSubscription = state |> value |> Map.containsKey usage.InternalResourceId 
                
                if not existingSubscription
                then 
                    state
                    |> addUnprocessableMessage 
                        { MessagePosition = meteringEvent.MessagePosition
                          EventData = UsageReported usage 
                          EventsToCatchup = None; Source = EventHub }
                else
                    let newMeterCollection =
                        state |> value
                        |> Map.change 
                            usage.InternalResourceId 
                            (Option.bind ((Meter.handleUsageEvent config (usage, meteringEvent.MessagePosition)) >> Some))

                    { state with MeterCollection = newMeterCollection }
            )
            |> setLastProcessed meteringEvent.MessagePosition
        | UnprocessableMessage m -> 
            state
            |> addUnprocessableMessage 
                { MessagePosition = meteringEvent.MessagePosition
                  EventData = UnprocessableMessage m 
                  EventsToCatchup = None; Source = EventHub }
            |> setLastProcessed meteringEvent.MessagePosition
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
                    |> setLastProcessed meteringEvent.MessagePosition
                            
    let handleMeteringEvents (config: MeteringConfigurationProvider) (state: MeterCollection option) (meteringEvents: MeteringEvent list) : MeterCollection =
        let state =
            match state with
            | None -> MeterCollection.Empty
            | Some meterCollection -> meterCollection

        meteringEvents |> List.fold (handleMeteringEvent config) state
