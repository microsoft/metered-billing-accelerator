// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Integration

open System
open System.Runtime.CompilerServices
open Microsoft.Extensions.DependencyInjection
open Metering.BaseTypes.EventHub
open Metering.BaseTypes

[<Extension>]
module MeteringAggregator =
    open MeterCollectionLogic

    let aggregate (meters: MeterCollection option) (e: EventHubProcessorEvent<MeterCollection option, MeteringUpdateEvent>) : MeterCollection option =
        let apply meterCollection eventHubEvent =
            eventHubEvent
            |> handleMeteringEvent meterCollection
            
        match meters with 
        | None ->
            match e with
            | PartitionInitializing (_, initialState)-> initialState
            | EHEvent x -> x |> apply MeterCollection.Empty |> Some
            | _ -> None
        | Some meterCollection ->
            match e with
            | EHEvent x -> x |> apply meterCollection |> Some
            | _ -> None

    [<Extension>]
    let createAggregator: Func<MeterCollection option, EventHubProcessorEvent<MeterCollection option, MeteringUpdateEvent>, MeterCollection option> =
        aggregate

    [<Extension>]
    let AddMeteringAggregatorConfigFromEnvironment (services: IServiceCollection) =
        services.AddSingleton(
            MeteringConfigurationProvider.create 
                (MeteringConnections.getFromEnvironment()) 
                (MarketplaceClient.SubmitUsage)
        )
