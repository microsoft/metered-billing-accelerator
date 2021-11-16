namespace Metering.Types

open Azure.Messaging.EventHubs.Consumer
open Metering.Types.EventHub

type MeterCollection = MeterCollection of Map<InternalResourceId, Meter>

type SomeMeterCollection = MeterCollection option
 
module MeterCollection =
    let value (MeterCollection x) = x
    let create x = (MeterCollection x)

    let empty : MeterCollection = Map.empty |> create
    
    let Uninitialized : (SomeMeterCollection) = None
    
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

    let getEventPosition (someMeters: MeterCollection option) : EventPosition =
        someMeters
        |> lastUpdate
        |> MessagePosition.startingPosition

    let usagesToBeReported (meters: MeterCollection) : MeteringAPIUsageEventDefinition list =
        if meters |> value |> Seq.isEmpty 
        then []
        else
            meters
            |> value
            |> Seq.map (fun x -> x.Value.UsageToBeReported)
            |> Seq.concat
            |> List.ofSeq

    let meterCollectionHandleMeteringEvent (config: MeteringConfigurationProvider) (state: MeterCollection) (meteringEvent: MeteringEvent) : MeterCollection =    
        // SubscriptionPurchased should add / overwrite existing entry
        // AggregatorBooted should trigger on all entries
        // UsageReported and UsageSubmittedToAPI should fire on the appropriate entry

        match meteringEvent.MeteringUpdateEvent with
        | SubscriptionPurchased s -> 
            state |> value
            |> Map.add s.Subscription.InternalResourceId (Meter.createNewSubscription s meteringEvent.MessagePosition)
        | AggregatorBooted ->
            state |> value
            |> Map.toSeq
            |> Seq.map(fun (k, v) -> (k, v |> Meter.handleAggregatorBooted config))
            |> Map.ofSeq
        | UsageSubmittedToAPI submission ->
            state |> value
            |> Map.change submission.Payload.ResourceId (Option.bind ((Meter.handleUsageSubmissionToAPI config submission) >> Some))
        | UsageReported usage -> 
            state |> value
            |> Map.change usage.InternalResourceId (Option.bind ((Meter.handleUsageEvent config (usage, meteringEvent.MessagePosition)) >> Some))
        |> create
            
    let meterCollectionHandleMeteringEvents (config: MeteringConfigurationProvider) (state: MeterCollection) (meteringEvents: MeteringEvent list) : MeterCollection =
        meteringEvents |> List.fold (meterCollectionHandleMeteringEvent config) state