namespace Metering.Types

open Azure.Messaging.EventHubs.Consumer
open Metering.Types.EventHub
open System.Runtime.CompilerServices

[<Extension>]
module MeterCollectionLogic =
    open MeterCollection

    let lastUpdateFromCollection (meters: MeterCollection) : MessagePosition option = 
        if meters |> value |> Seq.isEmpty 
        then None
        else
            meters
            |> value
            |> Map.toSeq
            |> Seq.maxBy (fun (_, meter) -> meter.LastProcessedMessage.SequenceNumber)
            |> (fun (_, meter) -> meter.LastProcessedMessage)
            |> Some

    let lastUpdate (someMeterCollection: MeterCollection option) : MessagePosition option = 
        match someMeterCollection with 
        | None -> None
        | Some meters -> 
            if meters |> value |> Seq.isEmpty 
            then None
            else
                meters
                |> value
                |> Map.toSeq
                |> Seq.maxBy (fun (_subType, meter) -> meter.LastProcessedMessage.SequenceNumber)
                |> (fun (_, meter) -> meter.LastProcessedMessage)
                |> Some

    [<Extension>]
    let MessagePositionAsString (meters: MeterCollection) : string =
        match meters |> lastUpdateFromCollection with
        | None -> "Earliest"
        | Some p -> $"{p.PartitionID |> PartitionID.value}/{p.SequenceNumber}"

    [<Extension>]
    let getEventPosition (someMeters: MeterCollection option) : EventPosition =
        someMeters
        |> lastUpdate
        |> MessagePosition.startingPosition

    [<Extension>]
    let getLastUpdate (meterCollection: MeterCollection) : string =
        meterCollection
        |> Some
        |> lastUpdate
        |> function
           | None -> ""
           | Some e -> $"{e.PartitionID |> PartitionID.value}-{e.SequenceNumber}"

    let usagesToBeReported (meters: MeterCollection) : MeteringAPIUsageEventDefinition list =
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

    let handleSubscriptionPurchased<'Key,'T when 'Key: comparison> (key: 'Key) (value: 'T) (table: Map<'Key,'T>) : Map<'Key,'T> =
        let ignoreAdditionalSubscriptionMessages = true

        let handle = 
            if ignoreAdditionalSubscriptionMessages 
            then addOnlyIfNotExists 
            else Map.add

        handle key value table

    let meterCollectionHandleMeteringEvent (config: MeteringConfigurationProvider) (state: MeterCollection) (meteringEvent: MeteringEvent) : MeterCollection =    
        // SubscriptionPurchased should add / overwrite existing entry
        // AggregatorBooted should trigger on all entries
        // UsageReported and UsageSubmittedToAPI should fire on the appropriate entry

        match meteringEvent.MeteringUpdateEvent with
        | SubscriptionPurchased s -> 
            state
            |> value
            |> handleSubscriptionPurchased s.Subscription.InternalResourceId (Meter.createNewSubscription s meteringEvent.MessagePosition)
            |> create
        | AggregatorBooted ->
            state
            |> value
            |> Map.toSeq
            |> Seq.map(fun (k, v) -> (k, v |> Meter.handleAggregatorBooted config))
            |> Map.ofSeq
            |> create
        | UsageSubmittedToAPI submission ->
            state
            |> value
            |> Map.change submission.Payload.ResourceId (Option.bind ((Meter.handleUsageSubmissionToAPI config submission) >> Some))
            |> create
        | UsageReported usage -> 
            let o = state |> value
            let n = o |> Map.change usage.InternalResourceId (Option.bind ((Meter.handleUsageEvent config (usage, meteringEvent.MessagePosition)) >> Some))



            if o = n
            then 
                printfn $"collection old: {Json.toStr 0 o}"
                printfn $"collection new: {Json.toStr 0 n}"
                printfn $"event: {Json.toStr 0 usage}"
                failwith "usage wasn't updated"

            n |> create
            
    let meterCollectionHandleMeteringEvents (config: MeteringConfigurationProvider) (state: MeterCollection) (meteringEvents: MeteringEvent list) : MeterCollection =
        meteringEvents |> List.fold (meterCollectionHandleMeteringEvent config) state
