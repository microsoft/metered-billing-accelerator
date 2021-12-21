namespace Metering.Types.EventHub

open System
open System.Runtime.CompilerServices
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Processor
open Metering.Types
open System.Collections.Generic

type SequenceNumber = int64

type PartitionID = PartitionID of string

[<Extension>]
module PartitionID =
    [<Extension>]
    let value (PartitionID x) = x
    let create x = (PartitionID x)

type PartitionKey = PartitionKey of string

[<Extension>]
module PartitionKey =
    [<Extension>]
    let value (PartitionKey x) = x
    let create x = (PartitionKey x)

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

    let create (partitionId: string) (eventData: EventData) : MessagePosition =
        { PartitionID = partitionId |> PartitionID.PartitionID
          SequenceNumber = eventData.SequenceNumber
          // Offset = eventData.Offset
          PartitionTimestamp = eventData.EnqueuedTime |> MeteringDateTime.fromDateTimeOffset }
    
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
        let lastEnqueuedTime = lastEnqueuedEvent.EnqueuedTime.Value |> MeteringDateTime.fromDateTimeOffset
        let lastEnqueuedEventSequenceNumber = lastEnqueuedEvent.SequenceNumber.Value
        let numberOfUnprocessedEvents = lastEnqueuedEventSequenceNumber - data.SequenceNumber
        let timeDiffBetweenCurrentEventAndMostRecentEvent = (lastEnqueuedTime - eventEnqueuedTime).TotalSeconds
        
        { LastSequenceNumber = lastSequenceNumber
          LastEnqueuedTime = lastEnqueuedTime
          NumberOfEvents = numberOfUnprocessedEvents
          TimeDeltaSeconds = timeDiffBetweenCurrentEventAndMostRecentEvent }

/// Indicate whether an event was read from EventHub, or from the associated capture storage.
type EventSource =
    | EventHub
    | Capture of BlobName:string

module EventSource =
    let toStr = 
        function
        | EventHub -> "EventHub"
        | Capture(BlobName=b) -> b

type EventHubEvent<'TEvent> =
    { MessagePosition: MessagePosition
      EventsToCatchup: EventsToCatchup option
      EventData: 'TEvent

      /// Indicate whether an event was read from EventHub, or from the associated capture storage.      
      Source: EventSource }

module EventHubEvent =
    let createFromEventHub (convert: EventData -> 'TEvent) (processEventArgs: ProcessEventArgs) : EventHubEvent<'TEvent> option =  
        if not processEventArgs.HasEvent
        then None
        else
            let catchUp = 
                processEventArgs.Partition.ReadLastEnqueuedEventProperties()
                |> EventsToCatchup.create processEventArgs.Data
                |> Some

            { MessagePosition = processEventArgs.Data |> MessagePosition.create processEventArgs.Partition.PartitionId 
              EventsToCatchup = catchUp
              EventData = processEventArgs.Data |> convert
              Source = EventHub }
            |> Some

    let createFromEventHubCapture (convert: EventData -> 'TEvent)  (partitionId: string) (blobName: string) (data: EventData) : EventHubEvent<'TEvent> option =  
        { MessagePosition = MessagePosition.create partitionId data
          EventsToCatchup = None
          EventData = data |> convert 
          Source = Capture(BlobName = blobName)}
        |> Some

type PartitionInitializing<'TState> =
    { PartitionID: PartitionID
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
        | PartitionInitializing e -> e.PartitionID
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

module internal Capture =
    type RehydratedFromCaptureEventData(
        blobName: string, eventBody: byte[], 
        properties: IDictionary<string, obj>, systemProperties: IReadOnlyDictionary<string, obj>, 
        sequenceNumber: int64, offset: int64, enqueuedTime: DateTimeOffset, partitionKey: string) =                 
        inherit EventData(new BinaryData(eventBody), properties, systemProperties, sequenceNumber, offset, enqueuedTime, partitionKey)
        member this.BlobName = blobName
    
    let getBlobName (e: EventData) : string option =
        match e with
        | :? RehydratedFromCaptureEventData -> (downcast e : RehydratedFromCaptureEventData) |> (fun x -> x.BlobName) |> Some
        | _ -> None 

module EventDataDummy = 
    let create (blobName: string) (eventBody: byte[]) (sequenceNumber: int64) (offset: int64)  (partitionKey: string) : EventData =
        new Capture.RehydratedFromCaptureEventData(blobName, eventBody, new Dictionary<string,obj>(), new Dictionary<string,obj>(), sequenceNumber, offset, DateTimeOffset.UtcNow, partitionKey)