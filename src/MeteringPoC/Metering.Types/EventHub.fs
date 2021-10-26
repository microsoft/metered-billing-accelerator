namespace Metering.Types.EventHub

open System
open Metering.Types
open Azure.Core
open Azure.Storage.Blobs
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Processor

type SequenceNumber = uint64

type PartitionID = string

type MessagePosition = 
    { PartitionID: PartitionID
      SequenceNumber: SequenceNumber
      PartitionTimestamp: MeteringDateTime }

type SeekPosition =
    | FromSequenceNumber of SequenceNumber: SequenceNumber 
    | Earliest
    | FromTail

type EventHubConnectionDetails =
    { Credential: TokenCredential 
      EventHubNamespace: string
      EventHubName: string
      ConsumerGroupName: string
      CheckpointStorage: BlobContainerClient }

type Event =
    { EventData: EventData
      LastEnqueuedEventProperties: LastEnqueuedEventProperties
      PartitionContext: PartitionContext }

type EventHubProcessorEvent =
    | Event of Event
    | EventHubError of ProcessErrorEventArgs
    | PartitionInitializing of PartitionInitializingEventArgs
    | PartitionClosing of PartitionClosingEventArgs
