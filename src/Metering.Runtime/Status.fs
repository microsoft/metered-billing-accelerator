// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Utils

open System.Threading
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Azure.Messaging.EventHubs.Consumer
open Metering.BaseTypes
open Metering.BaseTypes.EventHub
open Metering.Integration

/// Functions to interact with the system's state files in storage
[<Extension>]
module Status =
    type PartitionProps = PartitionProps of PartitionId:PartitionID * LastEnqueuedSequenceNumber:int64 * LastEnqueuedTime:MeteringDateTime * LastOffset:int64

    let private getPartitionIDs (client: EventHubConsumerClient) (cancellationToken: CancellationToken) = 
        task {
            let! props = client.GetEventHubPropertiesAsync(cancellationToken = cancellationToken)
            return props.PartitionIds |> Seq.map PartitionID.create
        }

    let private getPartitionProperties (client: EventHubConsumerClient) (cancellationToken: CancellationToken) (partitionIds: PartitionID seq) = 
        let getPartitionProps (client: EventHubConsumerClient) (cancellationToken: CancellationToken) (partitionId: PartitionID) = 
            task {
                let! p = client.GetPartitionPropertiesAsync(
                    partitionId = (partitionId.value), 
                    cancellationToken = cancellationToken)
                return (partitionId, PartitionProps(
                    PartitionId = partitionId,
                    LastEnqueuedSequenceNumber = p.LastEnqueuedSequenceNumber,
                    LastEnqueuedTime = (p.LastEnqueuedTime |> MeteringDateTime.fromDateTimeOffset),
                    LastOffset = p.LastEnqueuedOffset
                    ))
            }

        task {
            let! partitionProperties = partitionIds |> Seq.map (getPartitionProps client cancellationToken) |> Task.WhenAll
            return partitionProperties |> Seq.toArray |> Map.ofArray
        }

    let private getMessagePositions (config: MeteringConfigurationProvider) (cancellationToken: CancellationToken) (partitionIds: PartitionID seq) : Task<Map<PartitionID, MessagePosition option>> =
        let getState (config: MeteringConfigurationProvider) (cancellationToken: CancellationToken) (partitionId: PartitionID) =
            task {
                let! state = MeterCollectionStore.loadLastState config partitionId cancellationToken
                let pos = state |> Option.bind (fun m -> m.LastUpdate)
                return (partitionId, pos)
            }

        task {
            let! storedStates = partitionIds |> Seq.map (getState config cancellationToken) |> Task.WhenAll
            return storedStates |> Map.ofSeq
        }

    /// Download all snapshots for the given partition IDs.
    let private getStates (config: MeteringConfigurationProvider) (cancellationToken: CancellationToken) (partitionIds: PartitionID seq) : Task<Map<PartitionID, MeterCollection option>> =
        let getState partitionId =
            task {
                let! state = MeterCollectionStore.loadLastState config partitionId cancellationToken
                return (partitionId, state)
            }

        task {
            let! storedStates = partitionIds |> Seq.map getState |> Task.WhenAll
            return storedStates |> Map.ofSeq
        }

    [<Extension>]
    let fetchEventsToCatchup (config: MeteringConfigurationProvider) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) : Task<Map<PartitionID, EventsToCatchup>> =
        task {
            let client = config.MeteringConnections |> MeteringConnections.createEventHubConsumerClient
            let! partitionIds = getPartitionIDs client cancellationToken

            let! partitionProperties = partitionIds |> getPartitionProperties client cancellationToken           
            let! storedStates = partitionIds |> getMessagePositions config cancellationToken

            let diff (partitionId:PartitionID, lastEnqueuedSequenceNumber:int64, lastEnqueuedTime:MeteringDateTime, lastOffset:int64) (mp: MessagePosition) =
                { LastSequenceNumber = lastEnqueuedSequenceNumber
                  LastEnqueuedTime = lastEnqueuedTime
                  NumberOfEvents = lastEnqueuedSequenceNumber - mp.SequenceNumber
                  TimeDeltaSeconds = (lastEnqueuedTime - mp.PartitionTimestamp).TotalSeconds }

            return partitionIds
                |> Seq.map (fun partitionId ->
                    let part = partitionProperties |> Map.tryFind partitionId
                    let (stateLastMessagePos : MessagePosition option) = storedStates |> Map.tryFind partitionId |> Option.flatten

                    match (part,stateLastMessagePos) with
                    | Some (PartitionProps (pid,seqid,date,lastOffset)), Some statePosition ->  
                        (pid, { LastSequenceNumber = seqid
                                LastEnqueuedTime = date
                                NumberOfEvents = seqid - statePosition.SequenceNumber
                                TimeDeltaSeconds = (date - statePosition.PartitionTimestamp).TotalSeconds })
                    | Some (PartitionProps (pid,seqid,date,lastOffset)), None -> 
                        (pid, { LastSequenceNumber = seqid
                                LastEnqueuedTime = date
                                NumberOfEvents = seqid 
                                TimeDeltaSeconds = -1 })

                    | _ -> failwith $"Could not find {partitionId.value}: {part} {stateLastMessagePos}"
                )
                |> Map.ofSeq
        }

    [<Extension>]
    let fetchStates (config: MeteringConfigurationProvider) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        task {
            let client = config.MeteringConnections |> MeteringConnections.createEventHubConsumerClient
            let! partitionIds = getPartitionIDs client cancellationToken
            let! storedStates = partitionIds |> getStates config cancellationToken

            return storedStates
        }
