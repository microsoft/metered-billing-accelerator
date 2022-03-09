// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.EventHub

open System
open System.Collections.Generic
open Azure.Storage.Blobs
open Azure.Core
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Processor
open Metering.BaseTypes
open Metering.BaseTypes.EventHub

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

module EventHubIntegration =
    let createMessagePositionFromEventData (partitionId: PartitionID) (eventData: EventData) : MessagePosition =
        { PartitionID = partitionId
          SequenceNumber = eventData.SequenceNumber
          // Offset = eventData.Offset
          PartitionTimestamp = eventData.EnqueuedTime |> MeteringDateTime.fromDateTimeOffset }
 
    let createEventsToCatchup (data: EventData) (lastEnqueuedEvent: LastEnqueuedEventProperties) : EventsToCatchup =
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

    let createEventHubEventFromEventData (convert: EventData -> 'TEvent) (processEventArgs: ProcessEventArgs) : EventHubEvent<'TEvent> option =  
        if not processEventArgs.HasEvent
        then None
        else
            let catchUp = 
                processEventArgs.Partition.ReadLastEnqueuedEventProperties()
                |> createEventsToCatchup processEventArgs.Data
                |> Some

            { MessagePosition = processEventArgs.Data |> createMessagePositionFromEventData (processEventArgs.Partition.PartitionId |> PartitionID.create)
              EventsToCatchup = catchUp
              EventData = processEventArgs.Data |> convert
              Source = EventHub }
            |> Some

    let createFromEventHubCapture (convert: EventData -> 'TEvent)  (partitionId: PartitionID) (blobName: string) (data: EventData) : EventHubEvent<'TEvent> option =  
        { MessagePosition = createMessagePositionFromEventData partitionId data
          EventsToCatchup = None
          EventData = data |> convert 
          Source = Capture(BlobName = blobName)}
        |> Some

module Capture =
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

    let createEventDataFromBytes (blobName: string) (eventBody: byte[]) (sequenceNumber: int64) (offset: int64)  (partitionKey: string) : EventData =
        new RehydratedFromCaptureEventData(blobName, eventBody, new Dictionary<string,obj>(), new Dictionary<string,obj>(), sequenceNumber, offset, DateTimeOffset.UtcNow, partitionKey)

type EventHubName =
    { NamespaceName: string
      FullyQualifiedNamespace: string
      InstanceName: string }

module EventHubName =
    let create nameSpaceName instanceName =
        { NamespaceName = nameSpaceName
          FullyQualifiedNamespace = $"{nameSpaceName}.servicebus.windows.net"
          InstanceName = instanceName }

    let toStr (e: EventHubName) = $"{e.FullyQualifiedNamespace}/{e.InstanceName}"

type CaptureStorage = 
    { CaptureFileNameFormat: string
      Storage: BlobContainerClient }

type EventHubConfig =
    { EventHubName: EventHubName
      ConsumerGroupName: string
      CheckpointStorage: BlobContainerClient
      CaptureStorage: CaptureStorage option
      InfraStructureCredentials: TokenCredential }
