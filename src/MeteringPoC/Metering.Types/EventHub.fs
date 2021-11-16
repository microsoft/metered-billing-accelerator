namespace Metering.Types.EventHub

open System.Threading
open System.Threading.Tasks
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
    { ProcessEventArgs: ProcessEventArgs
      EventData: 't
      LastEnqueuedEventProperties: LastEnqueuedEventProperties
      PartitionContext: PartitionContext }

type PartitionInitializing<'state> =
    { PartitionInitializingEventArgs: PartitionInitializingEventArgs
      InitialState: 'state}

type PartitionClosing =
    { PartitionClosingEventArgs: PartitionClosingEventArgs }

type EventHubProcessorEvent<'state, 'event> =
    | Event of Event<'event>
    | EventHubError of ProcessErrorEventArgs
    | PartitionInitializing of PartitionInitializing<'state>
    | PartitionClosing of PartitionClosing

module EventHubProcessorEvent =
    let toStr<'state, 'event> (converter: 'event -> string) (e: EventHubProcessorEvent<'state, 'event>) : string =
        match e with
        | Event e -> $"Event: {e.PartitionContext.PartitionId}: {e.EventData |> converter}"
        | EventHubError e -> $"Error: {e.PartitionId}: {e.Exception.Message}"
        | PartitionInitializing e -> $"Initializing: {e.PartitionInitializingEventArgs.PartitionId}"
        | PartitionClosing e -> $"Closing: {e.PartitionClosingEventArgs.PartitionId}"
