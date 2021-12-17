namespace Metering.Types

open System
open Metering.Types.EventHub
open System.Runtime.CompilerServices

[<Extension>]
module MeteringAggregator =
    open MeterCollectionLogic

    let aggregate (config: MeteringConfigurationProvider) (meters: MeterCollection option) (e: EventHubProcessorEvent<MeterCollection option, MeteringUpdateEvent>) : MeterCollection option =
        let apply meterCollection eventHubEvent =
            eventHubEvent
            |> MeteringEvent.fromEventHubEvent 
            |> handleMeteringEvent config meterCollection
            
        match meters with 
        | None ->
            match e with
            | PartitionInitializing x -> x.InitialState
            | EventHubEvent x -> x |> apply MeterCollection.Empty |> Some
            | _ -> None
        | Some meterCollection ->
            match e with
            | EventHubEvent x -> x |> apply meterCollection |> Some
            | _ -> None

    [<Extension>]
    let createAggregator (config: MeteringConfigurationProvider) : Func<MeterCollection option, EventHubProcessorEvent<MeterCollection option, MeteringUpdateEvent>, MeterCollection option> =
        aggregate config
