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
        | Some meters -> 
            meters.LastUpdate 
            //if meters |> value |> Seq.isEmpty 
            //then None
            //else
            //    meters
            //    |> value
            //    |> Map.toSeq
            //    |> Seq.maxBy (fun (_subType, meter) -> meter.LastProcessedMessage.SequenceNumber)
            //    |> (fun (_, meter) -> meter.LastProcessedMessage)
            //    |> Some

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

    let private handleSubscriptionPurchased<'Key,'T when 'Key: comparison> (key: 'Key) (value: 'T) (table: Map<'Key,'T>) : Map<'Key,'T> =
        let ignoreAdditionalSubscriptionMessages = true

        let handle = 
            if ignoreAdditionalSubscriptionMessages 
            then addOnlyIfNotExists 
            else Map.add

        handle key value table

    let handleMeteringEvent (config: MeteringConfigurationProvider) (state: MeterCollection) (meteringEvent: MeteringEvent) : MeterCollection =    
        // SubscriptionPurchased should add / overwrite existing entry
        // AggregatorBooted should trigger on all entries
        // UsageReported and UsageSubmittedToAPI should fire on the appropriate entry

        match meteringEvent.MeteringUpdateEvent with
        | SubscriptionPurchased s -> 
            state
            |> value
            |> handleSubscriptionPurchased s.Subscription.InternalResourceId (Meter.createNewSubscription s meteringEvent.MessagePosition)
            |> create meteringEvent.MessagePosition
        | AggregatorBooted ->
            state
            |> value
            |> Map.toSeq
            |> Seq.map(fun (k, v) -> (k, v |> Meter.handleAggregatorBooted config))
            |> Map.ofSeq
            |> create meteringEvent.MessagePosition
        | UsageSubmittedToAPI submission ->
            state
            |> value
            //|> Map.change submission.Payload.ResourceId (Option.map (Meter.handleUsageSubmissionToAPI config submission))
            |> Map.change submission.Payload.ResourceId (Option.bind ((Meter.handleUsageSubmissionToAPI config submission) >> Some))
            |> create meteringEvent.MessagePosition
        | UsageReported usage -> 
            let o = state |> value
            let n = o |> Map.change usage.InternalResourceId (Option.bind ((Meter.handleUsageEvent config (usage, meteringEvent.MessagePosition)) >> Some))
            if o = n
            then 
                printfn $"collection old: {Json.toStr 0 o}"
                printfn $"collection new: {Json.toStr 0 n}"
                printfn $"event: {Json.toStr 0 usage}"
                failwith "usage wasn't updated"

            n |> create meteringEvent.MessagePosition
            
    let handleMeteringEvents (config: MeteringConfigurationProvider) (state: MeterCollection option) (meteringEvents: MeteringEvent list) : MeterCollection =
        let state =
            match state with
            | None -> MeterCollection.Empty
            | Some meterCollection -> meterCollection

        meteringEvents |> List.fold (handleMeteringEvent config) state
