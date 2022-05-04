// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes.EventHub

open System.Runtime.CompilerServices
open Metering.BaseTypes

type SequenceNumber = int64

type DifferenceBetweenTwoSequenceNumbers = SequenceNumber

type PartitionID = 
    private | PartitionID of string

    member this.value
        with get() : string =
            let v (PartitionID x) = x
            this |> v

    static member create (x: string) : PartitionID = (PartitionID x)

type PartitionKey = 
    private | PartitionKey of string

    member this.value
        with get() : string =
            let v (PartitionKey x) = x
            this |> v

    static member create (x: string) : PartitionKey = (PartitionKey x)

type PartitionIdentifier =
    | PartitionID of PartitionID
    | PartitionKey of PartitionKey

module PartitionIdentifier =
    let createId = PartitionID.create >> PartitionID
    let createKey = PartitionKey.create >> PartitionKey

type MessagePosition = 
    { PartitionID: PartitionID
      SequenceNumber: SequenceNumber
      // Offset: int64
      PartitionTimestamp: MeteringDateTime }

type StartingPosition =
    | Earliest
    | NextEventAfter of LastProcessedSequenceNumber: SequenceNumber * PartitionTimestamp: MeteringDateTime

module MessagePosition =
    let startingPosition (someMessagePosition: MessagePosition option) : StartingPosition =
        match someMessagePosition with
        | None -> Earliest
        | Some pos -> NextEventAfter(
            LastProcessedSequenceNumber = pos.SequenceNumber, 
            PartitionTimestamp = pos.PartitionTimestamp)
    
    let createData (partitionId: string) (sequenceNumber: int64) (partitionTimestamp: MeteringDateTime) =
        { PartitionID = partitionId |> PartitionID.create
          SequenceNumber = sequenceNumber
          PartitionTimestamp = partitionTimestamp }

type SeekPosition =
    | FromSequenceNumber of SequenceNumber: SequenceNumber 
    | Earliest
    | FromTail

type EventsToCatchup =
    { /// The sequence number observed the last event to be enqueued in the partition.
      LastSequenceNumber: SequenceNumber

      /// The date and time, in UTC, that the last event was enqueued in the partition.
      LastEnqueuedTime: MeteringDateTime

      NumberOfEvents: DifferenceBetweenTwoSequenceNumbers
      TimeDeltaSeconds: float }

/// Indicate whether an event was read from EventHub, or from the associated capture storage.
type EventSource =
    | EventHub
    | Capture of BlobName:string

    override this.ToString() =
        match this with
        | EventHub -> "EventHub"
        | Capture(BlobName=b) -> b 

type EventHubEvent<'TEvent> =
    { MessagePosition: MessagePosition
      EventsToCatchup: EventsToCatchup option
      EventData: 'TEvent

      /// Indicate whether an event was read from EventHub, or from the associated capture storage.      
      Source: EventSource }

    static member createEventHub (evnt: 'TEvent) (messagePosition: MessagePosition) (eventsToCatchup: EventsToCatchup option) : EventHubEvent<'TEvent> =
        { EventData = evnt
          MessagePosition = messagePosition
          EventsToCatchup = eventsToCatchup
          Source = EventHub }

    static member createFromCapture (evnt: 'TEvent) (messagePosition: MessagePosition) (eventsToCatchup: EventsToCatchup option) (blobName: string) : EventHubEvent<'TEvent> =
        { EventData = evnt
          MessagePosition = messagePosition
          EventsToCatchup = eventsToCatchup
          Source = EventHub }

type EventHubProcessorEvent<'TState, 'TEvent> =    
    | PartitionInitializing of PartitionID:PartitionID * InitialState:'TState
    | PartitionClosing of PartitionID
    | EHEvent of EventHubEvent<'TEvent>
    | EHError of PartitionID:PartitionID * Exception:exn

type EventHubName =
    { NamespaceName: string
      FullyQualifiedNamespace: string
      InstanceName: string }
    
    static member create nameSpaceName instanceName =
        { NamespaceName = nameSpaceName
          FullyQualifiedNamespace = $"{nameSpaceName}.servicebus.windows.net"
          InstanceName = instanceName }

    override this.ToString() = $"{this.FullyQualifiedNamespace}/{this.InstanceName}"
