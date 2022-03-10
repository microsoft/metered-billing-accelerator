// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.EventHub

open System
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Processor
open Metering.BaseTypes
open Metering.BaseTypes.EventHub

module EventHubIntegration =
    let partitionId<'TState, 'TEvent> (e: EventHubProcessorEvent<'TState, 'TEvent>) : PartitionID =
        match e with
        | PartitionInitializing (pi, _) -> pi
        | PartitionClosing pi -> pi
        | EventHubEvent e -> e.MessagePosition.PartitionID
        | EventHubError (pi, _) -> pi
        
    let toStr<'TState, 'TEvent> (converter: 'TEvent -> string) (e: EventHubProcessorEvent<'TState, 'TEvent>) : string =
        match e with
        | PartitionInitializing (pi, _)-> $"{pi |> PartitionID.value} Initializing"
        | PartitionClosing pi -> $"{pi |> PartitionID.value} Closing"
        | EventHubEvent e -> $"{e.MessagePosition.PartitionID |> PartitionID.value} Event: {e.EventData |> converter}"
        | EventHubError (pi, ex) -> $"{pi |> PartitionID.value} Error: {ex.Message}"
    
    let getEvent<'TState, 'TEvent> (e: EventHubProcessorEvent<'TState, 'TEvent>) : EventHubEvent<'TEvent> =
        match e with
        | EventHubEvent e -> e
        | _ -> raise (new ArgumentException(message = $"Not an {nameof(EventHubEvent)}", paramName = nameof(e)))

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

