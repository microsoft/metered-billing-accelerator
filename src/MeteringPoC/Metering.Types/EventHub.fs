namespace Metering.Types.EventHub

open Azure.Core
open Azure.Storage.Blobs
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Processor
open Metering.Types

type SequenceNumber = int64

type PartitionID = PartitionID of string

module PartitionID =
    let value (PartitionID x) = x
    let create x = (PartitionID x)

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

type EventHubEvent<'TEvent> =
    { ProcessEventArgs: ProcessEventArgs
      PartitionContext: PartitionContext
      LastEnqueuedEventProperties: LastEnqueuedEventProperties
      EventData: 'TEvent }

module EventHubEvent =
    let create (processEventArgs: ProcessEventArgs) (convert: EventData -> 'TEvent) : EventHubEvent<'TEvent> =  
        { ProcessEventArgs = processEventArgs
          PartitionContext = processEventArgs.Partition
          LastEnqueuedEventProperties = (processEventArgs.Partition.ReadLastEnqueuedEventProperties())
          EventData = processEventArgs.Data |> convert }

type PartitionInitializing<'TState> =
    { PartitionInitializingEventArgs: PartitionInitializingEventArgs
      InitialState: 'TState }

type PartitionClosing =
    { PartitionClosingEventArgs: PartitionClosingEventArgs }

type EventHubProcessorEvent<'TState, 'TEvent> =
    | EventHubEvent of EventHubEvent<'TEvent>
    | EventHubError of ProcessErrorEventArgs
    | PartitionInitializing of PartitionInitializing<'TState>
    | PartitionClosing of PartitionClosing

module EventHubProcessorEvent =
    let partitionId<'TState, 'TEvent> (e: EventHubProcessorEvent<'TState, 'TEvent>) : PartitionID =
        match e with
        | PartitionInitializing e -> e.PartitionInitializingEventArgs.PartitionId
        | PartitionClosing e -> e.PartitionClosingEventArgs.PartitionId
        | EventHubEvent e -> e.PartitionContext.PartitionId
        | EventHubError e -> e.PartitionId
        |> PartitionID.create

    let toStr<'TState, 'TEvent> (converter: 'TEvent -> string) (e: EventHubProcessorEvent<'TState, 'TEvent>) : string =
        let pi = e |> partitionId

        match e with
        | PartitionInitializing e -> $"{pi} Initializing"
        | EventHubEvent e -> $"{pi} Event: {e.EventData |> converter}"
        | EventHubError e -> $"{pi} Error: {e.Exception.Message}"
        | PartitionClosing e -> $"{pi} Closing"
    
