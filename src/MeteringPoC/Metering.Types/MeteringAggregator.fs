namespace Metering.Types

open System
open Metering.Types.EventHub

module MeteringAggregator =
    let aggregate (config: MeteringConfigurationProvider) (meters: MeterCollection option) (e: EventHubProcessorEvent<MeterCollection option, MeteringUpdateEvent>) : MeterCollection option =
        match meters with 
        | None ->
            match e with
            | PartitionInitializing x -> x.InitialState
            | _ -> None
        | Some meterCollection ->
            match e with
            | EventHubEvent eventHubEvent ->
                let meteringEvent : MeteringEvent =
                    { MeteringUpdateEvent = eventHubEvent.EventData
                      MessagePosition = eventHubEvent.MessagePosition
                      EventsToCatchup = eventHubEvent.EventsToCatchup }

                let result = 
                    meteringEvent
                    |> MeterCollection.meterCollectionHandleMeteringEvent config meterCollection
                    |> Some

                result
            | _ -> None


    let createAggregator (config: MeteringConfigurationProvider) : Func<MeterCollection option, EventHubProcessorEvent<MeterCollection option, MeteringUpdateEvent>, MeterCollection option> =
        aggregate config
