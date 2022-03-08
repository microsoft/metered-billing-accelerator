// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Integration

open System
open System.Runtime.CompilerServices
open Microsoft.Extensions.DependencyInjection
open Metering.EventHub
open Metering.BaseTypes

[<Extension>]
module MeteringAggregator =
    open MeterCollectionLogic

    let aggregate (config: TimeHandlingConfiguration) (meters: MeterCollection option) (e: EventHubProcessorEvent<MeterCollection option, MeteringUpdateEvent>) : MeterCollection option =
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
    let createAggregator (config: TimeHandlingConfiguration) : Func<MeterCollection option, EventHubProcessorEvent<MeterCollection option, MeteringUpdateEvent>, MeterCollection option> =
        aggregate config

    [<Extension>]
    let AddMeteringAggregatorConfigFromEnvironment (services: IServiceCollection) =
        services.AddSingleton(
            MeteringConfigurationProvider.create 
                (MeteringConnections.getFromEnvironment()) 
                (MarketplaceClient.SubmitUsage)
        )
