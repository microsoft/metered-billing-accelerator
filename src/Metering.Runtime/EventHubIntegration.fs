// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.EventHub

open System
open System.Runtime.CompilerServices
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Processor
open Metering.BaseTypes
open Metering.BaseTypes.EventHub

[<Extension>]
module EventHubIntegration =
    let partitionId<'TState, 'TEvent> (e: EventHubProcessorEvent<'TState, 'TEvent>) : PartitionID =
        match e with
        | PartitionInitializing (pi, _) -> pi
        | PartitionClosing pi -> pi
        | EventReceived e -> e.MessagePosition.PartitionID
        | ProcessingError (pi, _) -> pi
        
    let toStr<'TState, 'TEvent> (converter: 'TEvent -> string) (e: EventHubProcessorEvent<'TState, 'TEvent>) : string =
        match e with
        | PartitionInitializing (pi, _)-> $"{pi.value} Initializing"
        | PartitionClosing pi -> $"{pi.value} Closing"
        | EventReceived e -> $"{e.MessagePosition.PartitionID.value} Event: {e.EventData |> converter}"
        | ProcessingError (pi, ex) -> $"{pi.value} Error: {ex.Message}"
    
    let getEvent<'TState, 'TEvent> (e: EventHubProcessorEvent<'TState, 'TEvent>) : EventHubEvent<'TEvent> =
        match e with
        | EventReceived e -> e
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

    let CreateEventHubEventFromEventData (convert: Func<EventData,'TEvent>) (processEventArgs: ProcessEventArgs) : EventHubEvent<'TEvent> option =  
        createEventHubEventFromEventData (FuncConvert.FromFunc(convert)) processEventArgs 