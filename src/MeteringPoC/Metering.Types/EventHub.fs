namespace Metering.Types.EventHub

open System
open System.Runtime.CompilerServices
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Processor
open Metering.Types

type SequenceNumber = int64

type PartitionID = PartitionID of string

[<Extension>]
module PartitionID =
    [<Extension>]
    let value (PartitionID x) = x
    let create x = (PartitionID x)

type MessagePosition = 
    { PartitionID: PartitionID
      SequenceNumber: SequenceNumber
      Offset: int64
      PartitionTimestamp: MeteringDateTime }

module MessagePosition =
    let startingPosition (someMessagePosition: MessagePosition option) =
        match someMessagePosition with
        | None -> EventPosition.Earliest
        | Some p -> EventPosition.FromSequenceNumber(p.SequenceNumber, isInclusive = false) // If isInclusive=true, the specified event is included; otherwise the next event is returned.

    let create (partitionId: string) (eventData: EventData) : MessagePosition =
        { PartitionID = partitionId |> PartitionID.PartitionID
          SequenceNumber = eventData.SequenceNumber
          Offset = eventData.Offset
          PartitionTimestamp = eventData.EnqueuedTime |> MeteringDateTime.fromDateTimeOffset }
        
type SeekPosition =
    | FromSequenceNumber of SequenceNumber: SequenceNumber 
    | Earliest
    | FromTail

type EventsToCatchup =
    { /// The sequence number observed the last event to be enqueued in the partition.
      LastOffset: int64

      /// The sequence number observed the last event to be enqueued in the partition.
      LastSequenceNumber: int64

      /// The date and time, in UTC, that the last event was enqueued in the partition.
      LastEnqueuedTime: MeteringDateTime

      NumberOfEvents: int64
      TimeDeltaSeconds: float }

module EventsToCatchup =
    let create (data: EventData) (lastEnqueuedEvent: LastEnqueuedEventProperties) : EventsToCatchup =
        // if lastEnqueuedEvent = null or 
        let eventEnqueuedTime = data.EnqueuedTime |> MeteringDateTime.fromDateTimeOffset
        let lastSequenceNumber = lastEnqueuedEvent.SequenceNumber.Value
        let lastOffset = lastEnqueuedEvent.Offset.Value
        let lastEnqueuedTime = lastEnqueuedEvent.EnqueuedTime.Value |> MeteringDateTime.fromDateTimeOffset
        let lastEnqueuedEventSequenceNumber = lastEnqueuedEvent.SequenceNumber.Value
        
        { LastOffset = lastOffset
          LastSequenceNumber = lastSequenceNumber
          LastEnqueuedTime = lastEnqueuedTime
          NumberOfEvents = lastEnqueuedEventSequenceNumber - data.SequenceNumber
          TimeDeltaSeconds = ((lastEnqueuedTime - eventEnqueuedTime).TotalSeconds) }

type EventHubEvent<'TEvent> =
    { MessagePosition: MessagePosition
      EventsToCatchup: EventsToCatchup
      EventData: 'TEvent }

module EventHubEvent =
    let create (processEventArgs: ProcessEventArgs) (convert: EventData -> 'TEvent) : EventHubEvent<'TEvent> option =  
        if processEventArgs.HasEvent
        then 
            let lastEnqueuedEventProperties = processEventArgs.Partition.ReadLastEnqueuedEventProperties()
            { MessagePosition = MessagePosition.create processEventArgs.Partition.PartitionId processEventArgs.Data
              EventsToCatchup = EventsToCatchup.create processEventArgs.Data lastEnqueuedEventProperties
              EventData = processEventArgs.Data |> convert }
            |> Some
        else None

type PartitionInitializing<'TState> =
    { PartitionInitializingEventArgs: PartitionInitializingEventArgs
      InitialState: 'TState }

type PartitionClosing =
    { PartitionClosingEventArgs: PartitionClosingEventArgs }

type EventHubProcessorEvent<'TState, 'TEvent> =    
    | PartitionInitializing of PartitionInitializing<'TState>
    | PartitionClosing of PartitionClosing
    | EventHubEvent of EventHubEvent<'TEvent>
    | EventHubError of PartitionID:PartitionID * Exception:exn

module EventHubProcessorEvent =
    let partitionId<'TState, 'TEvent> (e: EventHubProcessorEvent<'TState, 'TEvent>) : PartitionID =
        match e with
        | PartitionInitializing e -> e.PartitionInitializingEventArgs.PartitionId |> PartitionID.create
        | PartitionClosing e -> e.PartitionClosingEventArgs.PartitionId |> PartitionID.create
        | EventHubEvent e -> e.MessagePosition.PartitionID
        | EventHubError (partitionID, _) -> partitionID
        
    let toStr<'TState, 'TEvent> (converter: 'TEvent -> string) (e: EventHubProcessorEvent<'TState, 'TEvent>) : string =
        let pi = e |> partitionId

        match e with
        | PartitionInitializing e -> $"{pi} Initializing"
        | PartitionClosing e -> $"{pi} Closing"
        | EventHubEvent e -> $"{pi} Event: {e.EventData |> converter}"
        | EventHubError (partitionId,ex) -> $"{pi} Error: {ex.Message}"
    
    let getEvent<'TState, 'TEvent> (e: EventHubProcessorEvent<'TState, 'TEvent>) : EventHubEvent<'TEvent> =
        match e with
        | EventHubEvent e -> e
        | _ -> raise (new ArgumentException(message = $"Not an {nameof(EventHubEvent)}", paramName = nameof(e)))
