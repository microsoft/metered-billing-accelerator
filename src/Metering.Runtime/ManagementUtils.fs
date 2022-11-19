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
        config.MeteringConnections
        |> CaptureProcessor.readAllEvents CaptureProcessor.toMeteringUpdateEvent messagePosition.PartitionID CancellationToken.None
        |> Seq.filter (fun e -> e.MessagePosition.SequenceNumber < messagePosition.SequenceNumber)
        |> Seq.scan MeterCollectionLogic.handleMeteringEvent MeterCollection.Empty
        |> Seq.last

    [<Extension>]
    let recreateLatestStateFromEventHubCapture (config: MeteringConfigurationProvider) (partitionId: PartitionID)  =
        let cancellationToken = CancellationToken.None

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

    [<Extension>]
    let getPartitionCount (config: MeteringConfigurationProvider) (cancellationToken: CancellationToken) : Task<string seq> = 
        task {
            let! props = config.MeteringConnections.createEventHubConsumerClient().GetEventHubPropertiesAsync(CancellationToken.None)
            return props.PartitionIds |> Array.toSeq
        }

    let getConfigurations (config: MeteringConfigurationProvider) (cancellationToken: CancellationToken) =
        let aMap f x = async {
            let! a = x
            return f a }

        task {
            let! partitionIDs = getPartitionCount config cancellationToken

            let fetchTasks = 
                partitionIDs
                |> Seq.map PartitionID.create
                |> Seq.map (fun partitionId -> MeterCollectionStore.loadLastState config partitionId cancellationToken)
                |> Seq.toArray

            let! x = Task.WhenAll<MeterCollection option>(fetchTasks)

            return
                x
                |> Array.choose id
                |> Array.map (fun x -> (x.LastUpdate.Value.PartitionID, x))
                |> Map.ofArray
        }