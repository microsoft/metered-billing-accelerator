// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Utils

open System.Threading
open System.Threading.Tasks
open System.Runtime.CompilerServices
open Metering.EventHub
open Metering.BaseTypes
open Metering.BaseTypes.EventHub
open Metering.Integration

[<Extension>]
module ManagementUtils =
    [<Extension>]
    let recreateStateFromEventHubCapture (config: MeteringConfigurationProvider) (messagePosition: MessagePosition) : MeterCollection =
        let cancellationToken = CancellationToken.None

        let aggregate = MeterCollectionLogic.handleMeteringEvent
        let initialState : MeterCollection option = MeterCollection.Uninitialized

        config.MeteringConnections
        |> CaptureProcessor.readAllEvents CaptureProcessor.toMeteringUpdateEvent messagePosition.PartitionID cancellationToken 
        |> Seq.filter (fun e -> e.MessagePosition.SequenceNumber < messagePosition.SequenceNumber)
        |> Seq.scan aggregate MeterCollection.Empty
        |> Seq.last

    [<Extension>]
    let recreateLatestStateFromEventHubCapture (config: MeteringConfigurationProvider) (partitionId: PartitionID)  =
        let cancellationToken = CancellationToken.None

        let initialState : MeterCollection option = MeterCollection.Uninitialized

        let x = 
            config.MeteringConnections
            |> CaptureProcessor.readAllEvents CaptureProcessor.toMeteringUpdateEvent partitionId cancellationToken 
            |> Seq.scan MeterCollectionLogic.handleMeteringEvent MeterCollection.Empty
            |> Seq.last

        (MeterCollectionStore.storeLastState config x cancellationToken).Wait()

    [<Extension>]
    let showEventsFromPositionInEventHub (config: MeteringConfigurationProvider) (partitionId: PartitionID) (start: MeteringDateTime) =
        config.MeteringConnections
        |> CaptureProcessor.readEventsFromTime CaptureProcessor.toMeteringUpdateEvent partitionId start CancellationToken.None
        
    let getUnsubmittedMeters (config: MeteringConfigurationProvider) (partitionId: PartitionID) (cancellationToken: CancellationToken) : Task<MarketplaceRequest seq> =
        task {
            let! state = 
                MeterCollectionStore.loadLastState 
                    config partitionId cancellationToken

            return
                match state with
                | Some m -> m.metersToBeSubmitted
                | None -> Seq.empty            

        }