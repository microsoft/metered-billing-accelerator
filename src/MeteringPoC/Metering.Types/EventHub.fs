namespace Metering.Types.EventHub

open Azure.Core
open Azure.Storage.Blobs
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Processor
open Metering.Types

type SequenceNumber = int64

type PartitionID = string

type MessagePosition = 
    { PartitionID: PartitionID
      SequenceNumber: SequenceNumber
      PartitionTimestamp: MeteringDateTime }

module MessagePosition =
    let startingPosition (someMessagePosition: MessagePosition option) =
        match someMessagePosition with
        | Some p -> EventPosition.FromSequenceNumber(p.SequenceNumber + 1L)
        | None -> EventPosition.Earliest
        
type SeekPosition =
    | FromSequenceNumber of SequenceNumber: SequenceNumber 
    | Earliest
    | FromTail

type EventsToCatchup =
    { NumberOfEvents: int64
      TimeDelta: System.TimeSpan }

type EventHubConnectionDetails =
    { Credential: TokenCredential 
      EventHubNamespace: string
      EventHubName: string
      ConsumerGroupName: string
      CheckpointStorage: BlobContainerClient }

module EventHubConnectionDetails =
    let createProcessor (eventHubConnectionDetails: EventHubConnectionDetails) : EventProcessorClient =
        let clientOptions = new EventProcessorClientOptions()
        clientOptions.TrackLastEnqueuedEventProperties <- true
        clientOptions.PrefetchCount <- 100

        new EventProcessorClient(
            checkpointStore = eventHubConnectionDetails.CheckpointStorage,
            consumerGroup = eventHubConnectionDetails.ConsumerGroupName,
            fullyQualifiedNamespace = eventHubConnectionDetails.EventHubNamespace,
            eventHubName = eventHubConnectionDetails.EventHubName,
            credential = eventHubConnectionDetails.Credential,
            clientOptions = clientOptions)

type Event<'t> =
    { EventData: 't
      LastEnqueuedEventProperties: LastEnqueuedEventProperties
      PartitionContext: PartitionContext }

type EventHubProcessorEvent<'t> =
    | Event of Event<'t>
    | EventHubError of ProcessErrorEventArgs
    | PartitionInitializing of PartitionInitializingEventArgs
    | PartitionClosing of PartitionClosingEventArgs

module EventHubProcessorEvent =
    let toStr<'t> (converter: 't -> string) (e: EventHubProcessorEvent<'t>) : string =
        match e with
        | Event e -> $"Event: {e.PartitionContext.PartitionId}: {e.EventData |> converter}"
        | EventHubError e -> $"Error: {e.PartitionId}: {e.Exception.Message}"
        | PartitionInitializing e -> $"Initializing: {e.PartitionId}"
        | PartitionClosing e -> $"Closing: {e.PartitionId}"
