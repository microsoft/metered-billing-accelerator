namespace Metering.Types.EventHub

open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Processor
open Metering.Types
open System.Runtime.CompilerServices
open NodaTime

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
        | Some p -> EventPosition.FromSequenceNumber(p.SequenceNumber + 1L)
        | None -> EventPosition.Earliest
    
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
    { LastOffset: int64
      LastSequenceNumber: int64
      LastEnqueuedTime: MeteringDateTime
      LastReceivedTime: MeteringDateTime
      NumberOfEvents: int64
      TimeDeltaSeconds: float }

module EventsToCatchup =
    let create (data: EventData) (lastEnqueuedEvent: LastEnqueuedEventProperties) : EventsToCatchup =
        // if lastEnqueuedEvent = null or 
        let eventEnqueuedTime = data.EnqueuedTime |> MeteringDateTime.fromDateTimeOffset
        let lastSequenceNumber = lastEnqueuedEvent.SequenceNumber.Value
        let lastOffset = lastEnqueuedEvent.Offset.Value
        let lastEnqueuedTime = lastEnqueuedEvent.EnqueuedTime.Value |> MeteringDateTime.fromDateTimeOffset
        let lastReceivedTime = lastEnqueuedEvent.LastReceivedTime.Value|> MeteringDateTime.fromDateTimeOffset
        let lastEnqueuedEventSequenceNumber = lastEnqueuedEvent.SequenceNumber.Value
        
        { LastOffset = lastOffset
          LastSequenceNumber = lastSequenceNumber
          LastEnqueuedTime = lastEnqueuedTime
          LastReceivedTime = lastReceivedTime
          NumberOfEvents = lastEnqueuedEventSequenceNumber - data.SequenceNumber
          TimeDeltaSeconds = ((lastReceivedTime - eventEnqueuedTime).TotalSeconds) }

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
    | EventHubEvent of EventHubEvent<'TEvent>
    | EventHubError of (PartitionID * exn)
    | PartitionInitializing of PartitionInitializing<'TState>
    | PartitionClosing of PartitionClosing

module EventHubProcessorEvent =
    let partitionId<'TState, 'TEvent> (e: EventHubProcessorEvent<'TState, 'TEvent>) : PartitionID =
        match e with
        | PartitionInitializing e -> e.PartitionInitializingEventArgs.PartitionId |> PartitionID.create
        | PartitionClosing e -> e.PartitionClosingEventArgs.PartitionId |> PartitionID.create
        | EventHubEvent e -> e.MessagePosition.PartitionID
        | EventHubError (partitionId,_ex) -> partitionId
        
    let toStr<'TState, 'TEvent> (converter: 'TEvent -> string) (e: EventHubProcessorEvent<'TState, 'TEvent>) : string =
        let pi = e |> partitionId

        match e with
        | PartitionInitializing e -> $"{pi} Initializing"
        | EventHubEvent e -> $"{pi} Event: {e.EventData |> converter}"
        | EventHubError (partitionId,ex) -> $"{pi} Error: {ex.Message}"
        | PartitionClosing e -> $"{pi} Closing"
    
