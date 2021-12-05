namespace Metering.Types

open System
open Metering.Types.EventHub
open System.Runtime.CompilerServices

[<Extension>]
module MeteringAggregator =
    open MeterCollectionLogic

    let aggregate (config: MeteringConfigurationProvider) (meters: MeterCollection option) (e: EventHubProcessorEvent<MeterCollection option, MeteringUpdateEvent>) : MeterCollection option =
        match meters with 
        | None ->
            match e with
            | PartitionInitializing x -> 
                x.InitialState
            | EventHubEvent eventHubEvent ->
                { MeteringUpdateEvent = eventHubEvent.EventData
                  MessagePosition = eventHubEvent.MessagePosition
                  EventsToCatchup = eventHubEvent.EventsToCatchup }
                |> handleMeteringEvent config MeterCollection.Empty
                |> Some
            | _ -> None
        | Some meterCollection ->
            match e with
            | EventHubEvent eventHubEvent ->
                { MeteringUpdateEvent = eventHubEvent.EventData
                  MessagePosition = eventHubEvent.MessagePosition
                  EventsToCatchup = eventHubEvent.EventsToCatchup }
                |> handleMeteringEvent config meterCollection
                |> Some
            | _ -> None

    [<Extension>]
    let createAggregator (config: MeteringConfigurationProvider) : Func<MeterCollection option, EventHubProcessorEvent<MeterCollection option, MeteringUpdateEvent>, MeterCollection option> =
        aggregate config

