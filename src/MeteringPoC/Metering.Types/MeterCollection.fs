namespace Metering.Types

open Metering.Types.EventHub

type MeterCollection = Map<SubscriptionType, Meter>

module MeterCollection =
    let empty : MeterCollection = Map.empty

    let lastUpdate (meters: MeterCollection) : MessagePosition option = 
        if meters |> Seq.isEmpty 
        then None
        else
            meters
            |> Map.toSeq
            |> Seq.maxBy (fun (_subType, meter) -> meter.LastProcessedMessage.SequenceNumber)
            |> (fun (_, meter) -> meter.LastProcessedMessage)
            |> Some

    let usagesToBeReported (meters: MeterCollection) : MeteringAPIUsageEventDefinition list =
        if meters |> Seq.isEmpty 
        then []
        else
            meters
            |> Seq.map (fun x -> x.Value.UsageToBeReported)
            |> Seq.concat
            |> List.ofSeq

    let meterCollectionHandleMeteringEvent (config: MeteringConfigurationProvider) (state: MeterCollection) (meteringEvent: MeteringEvent) : MeterCollection =    
        // SubscriptionPurchased should add / overwrite existing entry
        // AggregatorBooted should trigger on all entries
        // UsageReported and UsageSubmittedToAPI should fire on the appropriate entry

        match meteringEvent.MeteringUpdateEvent with
        | SubscriptionPurchased s -> 
            state
            |> Map.add s.Subscription.SubscriptionType (Meter.createNewSubscription s meteringEvent.MessagePosition)
        | AggregatorBooted ->
            state
            |> Map.toSeq
            |> Seq.map(fun (k, v) -> (k, v |> Meter.handleAggregatorBooted config))
            |> Map.ofSeq
        | UsageSubmittedToAPI submission ->
            state
            |> Map.change submission.Payload.SubscriptionType (Option.bind ((Meter.handleUsageSubmissionToAPI config submission) >> Some))
        | UsageReported usage -> 
            state
            |> Map.change usage.Scope (Option.bind ((Meter.handleUsageEvent config (usage, meteringEvent.MessagePosition)) >> Some))
            
    let meterCollectionHandleMeteringEvents (config: MeteringConfigurationProvider) (state: MeterCollection) (meteringEvents: MeteringEvent list) : MeterCollection =
        meteringEvents |> List.fold (meterCollectionHandleMeteringEvent config) state