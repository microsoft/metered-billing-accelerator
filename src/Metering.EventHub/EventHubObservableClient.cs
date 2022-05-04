// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Integration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Processor;
using Azure.Messaging.EventHubs.Consumer;
using Metering.BaseTypes;
using Metering.BaseTypes.EventHub;
using Microsoft.FSharp.Core;
using MeteringDateTime = NodaTime.ZonedDateTime;
using SequenceNumber = System.Int64;

// https://www.fareez.info/blog/emulating-discriminated-unions-in-csharp-using-records/
internal record EventHubCaptureConf { };
internal record CanReadEverythingFromEventHub (EventPosition EventPosition) : EventHubCaptureConf;
internal record ReadFromEventHubCaptureAndThenEventHub (SequenceNumber LastProcessedSequenceNumber, MeteringDateTime LastProcessedEventTimestamp) : EventHubCaptureConf;
internal record ReadFromEventHubCaptureBeginningAndThenEventHub() : EventHubCaptureConf;

public static class EventHubObservableClient
{
    private static IObservable<EventHubProcessorEvent<TState, TEvent>> CreateInternal<TState, TEvent>(
        ILogger logger,
        Func<PartitionInitializingEventArgs, CancellationToken, Task<TState>> determineInitialState,
        Func<TState, StartingPosition> determinePositionFromState,
        Func<EventProcessorClient> newEventProcessorClient,                                                                           // config.MeteringConnections.createEventProcessorClient
        Func<EventHubConsumerClient> newEventHubConsumerClient,                                                                       // config.MeteringConnections.createEventHubConsumerClient
        Func<EventData, TEvent> eventDataToEvent,                                                                                     // CaptureProcessor.toMeteringUpdateEvent
        Func<Func<EventData, TEvent>, ProcessEventArgs, FSharpOption<EventHubEvent<TEvent>>> createEventHubEventFromEventData,        // EventHubIntegration.createEventHubEventFromEventData
        Func<Func<EventData, TEvent>, PartitionID, CancellationToken, IEnumerable<EventHubEvent<TEvent>>> readAllEvents,              // CaptureProcessor.readAllEvents
        Func<Func<EventData, TEvent>, MessagePosition, CancellationToken, IEnumerable<EventHubEvent<TEvent>>> readEventsFromPosition, // CaptureProcessor.readEventsFromPosition
        CancellationToken cancellationToken)
    {
        void registerCancellationMessage(CancellationToken ct, string message)
        {
            ct.Register(() => logger.LogWarning(message));
        }
        
        registerCancellationMessage(cancellationToken, "outer cancellationToken pulled");

        IDisposable csharpFunction(IObserver<EventHubProcessorEvent<TState, TEvent>> o)
        {
            logger.LogInformation($"csharpFunction called");

            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var innerCancellationToken = cts.Token;
            registerCancellationMessage(innerCancellationToken, "innerCancellationToken is Cancelled");

            Task ProcessEvent(ProcessEventArgs processEventArgs)
            {
                logger.LogInformation($"ProcessEvent called: {processEventArgs.Partition.PartitionId}");
                try
                {
                    FSharpOption<EventHubEvent<TEvent>> x = createEventHubEventFromEventData(eventDataToEvent, processEventArgs);
                    if (x.IsSome())
                    {
                        o.OnNext(EventHubProcessorEvent<TState, TEvent>.NewEventReceived(x.Value));
                    }
                    else
                    {
                        LastEnqueuedEventProperties catchUp = processEventArgs.Partition.ReadLastEnqueuedEventProperties();
                        var msg = $"Didn't find events: PartitionId {processEventArgs.Partition.PartitionId} SequenceNumber {catchUp.SequenceNumber} EnqueuedTime {catchUp.EnqueuedTime} LastReceivedTime {catchUp.LastReceivedTime} ###############";
                        logger.LogDebug(msg);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.Message);
                }
                return Task.CompletedTask;
            }

            Task ProcessError(ProcessErrorEventArgs processErrorEventArgs)
            {
                logger.LogError($"ProcessError");
                try
                {
                    o.OnError(processErrorEventArgs.Exception);
                }
                catch (Exception e)
                {
                    logger.LogError($"ProcessError Exception {e.Message}");
                }
                return Task.CompletedTask;
            }

            Task PartitionClosing(PartitionClosingEventArgs partitionClosingEventArgs)
            {
                try
                {
                    if (partitionClosingEventArgs.CancellationToken.IsCancellationRequested)
                    {
                        logger.LogError($"PartitionClosing partitionClosingEventArgs.CancellationToken.IsCancellationRequested");
                    }

                    if (partitionClosingEventArgs.Reason == ProcessingStoppedReason.OwnershipLost)
                    {
                        logger.LogError($"{partitionClosingEventArgs.PartitionId}: ProcessingStoppedReason.OwnershipLost");
                    }
                    else
                    {
                        logger.LogError($"{partitionClosingEventArgs.PartitionId}: ProcessingStoppedReason.Shutdown");
                    }

                    o.OnCompleted();
                }
                catch (Exception e)
                {
                    logger.LogError($"ProcessError Exception {e.Message}");
                }
                return Task.CompletedTask;
            }

            async Task PartitionInitializing(PartitionInitializingEventArgs partitionInitializingEventArgs)
            {
                logger.LogInformation($"PartitionInitializing: {partitionInitializingEventArgs.PartitionId}");
                var partitionIdStr = partitionInitializingEventArgs.PartitionId;

                TState initialState = await determineInitialState(partitionInitializingEventArgs, innerCancellationToken);
                StartingPosition initialPosition = determinePositionFromState(initialState);

                async Task<EventHubCaptureConf> getEventHubCaptureConf()
                {
                    if (initialPosition.IsEarliest)
                    {
                        return new ReadFromEventHubCaptureBeginningAndThenEventHub();
                    }
                    else
                    {
                        var lastProcessedEventSequenceNumber = ((StartingPosition.NextEventAfter)initialPosition).LastProcessedSequenceNumber;
                        var lastProcessedEventTimestamp = ((StartingPosition.NextEventAfter)initialPosition).PartitionTimestamp;

                        // Let's briefly check if the desired event is still avail in EventHub,
                        // otherwise we need to crawl through EventHub Capture
                        var consumerClient = newEventHubConsumerClient();

                        var partitionProps = await consumerClient.GetPartitionPropertiesAsync(
                            partitionId: partitionIdStr, cancellationToken: cancellationToken);

                        long desiredEvent = lastProcessedEventSequenceNumber + 1;
                        long firstOneWeCanRead = partitionProps.BeginningSequenceNumber;
                        bool desiredEventIsNotAvailableInEventHub = desiredEvent < firstOneWeCanRead;

                        if (desiredEventIsNotAvailableInEventHub)
                        {
                            return new ReadFromEventHubCaptureAndThenEventHub(
                                LastProcessedSequenceNumber: ((StartingPosition.NextEventAfter)initialPosition).LastProcessedSequenceNumber,
                                LastProcessedEventTimestamp: ((StartingPosition.NextEventAfter)initialPosition).PartitionTimestamp);
                        }
                        else
                        {
                            // If isInclusive=true, the specified event (nextEventAfter) is included; otherwise the next event is returned.
                            // We *cannot* do 
                            //     EventPosition.FromSequenceNumber(nextEventAfter + 1L, isInclusive = true)
                            // , as that crashes if nextEventAfter is the last one
                            return new CanReadEverythingFromEventHub(
                                EventPosition.FromSequenceNumber(
                                    sequenceNumber: lastProcessedEventSequenceNumber,
                                    isInclusive: false));
                        }
                    }
                }

                var eventHubStartPosition = await getEventHubCaptureConf();

                Task OnlyEventHub(CanReadEverythingFromEventHub x)
                {
                    o.OnNext(EventHubProcessorEvent<TState, TEvent>.NewPartitionInitializing(
                        PartitionID.create(partitionIdStr), initialState));
                    partitionInitializingEventArgs.DefaultStartingPosition = x.EventPosition;
                    return Task.CompletedTask;
                }

                Task AllCaptureThenEventHub(ReadFromEventHubCaptureBeginningAndThenEventHub x)
                {
                    o.OnNext(EventHubProcessorEvent<TState, TEvent>.NewPartitionInitializing(
                        PartitionID.create(partitionIdStr), initialState));
                    PartitionID partitionId = PartitionID.create(partitionInitializingEventArgs.PartitionId);

                    var lastProcessedEventReadFromCaptureSequenceNumber =
                        readAllEvents(
                            eventDataToEvent, partitionId, cancellationToken)
                        .Select(e =>
                        {
                            o.OnNext(EventHubProcessorEvent<TState, TEvent>.NewEventReceived(e));
                            return e.MessagePosition.SequenceNumber;
                        })
                        .LastOrDefault(defaultValue: -1);

                    if (lastProcessedEventReadFromCaptureSequenceNumber == -1)
                    {
                        partitionInitializingEventArgs.DefaultStartingPosition = 
                            EventPosition.Earliest;
                    }
                    else
                    {
                        partitionInitializingEventArgs.DefaultStartingPosition =
                            EventPosition.FromSequenceNumber(
                                sequenceNumber: lastProcessedEventReadFromCaptureSequenceNumber,
                                isInclusive: false);
                    }

                    return Task.CompletedTask;
                }

                Task SomeCaptureThenEventHub(ReadFromEventHubCaptureAndThenEventHub x)
                {
                    o.OnNext(EventHubProcessorEvent<TState, TEvent>.NewPartitionInitializing(
                        PartitionID.create(partitionIdStr), initialState));

                    var messagePosition = MessagePosition.create(
                        partitionId: partitionIdStr,
                        sequenceNumber: x.LastProcessedSequenceNumber,
                        partitionTimestamp: x.LastProcessedEventTimestamp);

                    var lastProcessedEventReadFromCaptureSequenceNumber =
                        readEventsFromPosition(
                            eventDataToEvent, messagePosition, cancellationToken)
                        .Select(e =>
                        {
                            o.OnNext(EventHubProcessorEvent<TState, TEvent>.NewEventReceived(e));
                            return e.MessagePosition.SequenceNumber;
                        })
                        .LastOrDefault(defaultValue: -1);

                    if (lastProcessedEventReadFromCaptureSequenceNumber == -1)
                    {
                        partitionInitializingEventArgs.DefaultStartingPosition =
                            EventPosition.Earliest;
                    }
                    else
                    {
                        partitionInitializingEventArgs.DefaultStartingPosition =
                            EventPosition.FromSequenceNumber(
                                sequenceNumber: lastProcessedEventReadFromCaptureSequenceNumber,
                                isInclusive: false);
                    }

                    return Task.CompletedTask;
                }

                await (eventHubStartPosition switch
                {
                    CanReadEverythingFromEventHub x => OnlyEventHub(x),
                    ReadFromEventHubCaptureBeginningAndThenEventHub x => AllCaptureThenEventHub(x),
                    ReadFromEventHubCaptureAndThenEventHub x => SomeCaptureThenEventHub(x),
                    _ => throw new NotSupportedException($"Unknown type {eventHubStartPosition.GetType().FullName} of {nameof(eventHubStartPosition)}"),
                });
            }

            async Task createTask()
            {
                logger.LogInformation($"createTask called");
                var processor = newEventProcessorClient();
                try
                {
                    processor.ProcessEventAsync += ProcessEvent;
                    processor.ProcessErrorAsync += ProcessError;
                    processor.PartitionInitializingAsync += PartitionInitializing;
                    processor.PartitionClosingAsync += PartitionClosing;

                    try
                    {
                        await processor.StartProcessingAsync(cancellationToken);
                        logger.LogInformation($"createTask / processor.StartProcessingAsync called");
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                        o.OnCompleted();
                        await processor.StopProcessingAsync(cancellationToken);
                    }
                    catch (TaskCanceledException) { }
                    catch (Exception ex) { o.OnError(ex); }
                }
                catch (Exception)
                {
                    processor.ProcessEventAsync -= ProcessEvent;
                    processor.ProcessErrorAsync -= ProcessError;
                    processor.PartitionInitializingAsync -= PartitionInitializing;
                    processor.PartitionClosingAsync -= PartitionClosing;
                }
            }

            _ = Task.Run(createTask, cancellationToken);

            return new CancellationDisposable(cts);
        }

        logger.LogInformation($"Now returning the observable...");

        return Observable.Create<EventHubProcessorEvent<TState, TEvent>>(csharpFunction);   
    }

    public static IObservable<IGroupedObservable<PartitionID, EventHubProcessorEvent<TState, TEvent>>> Create<TState, TEvent>(
        ILogger logger,
        Func<EventHubProcessorEvent<TState, TEvent>, PartitionID> getPartitionId,                                                     // EventHubIntegration.partitionId
        Func<EventProcessorClient> newEventProcessorClient,                                                                           // config.MeteringConnections.createEventProcessorClient
        Func<EventHubConsumerClient> newEventHubConsumerClient,                                                                       // config.MeteringConnections.createEventHubConsumerClient
        Func<EventData, TEvent> eventDataToEvent,                                                                                     // CaptureProcessor.toMeteringUpdateEvent
        Func<Func<EventData, TEvent>, ProcessEventArgs, FSharpOption<EventHubEvent<TEvent>>> createEventHubEventFromEventData,        // EventHubIntegration.createEventHubEventFromEventData
        Func<Func<EventData, TEvent>, PartitionID, CancellationToken, IEnumerable<EventHubEvent<TEvent>>> readAllEvents,              // CaptureProcessor.readAllEvents
        Func<Func<EventData, TEvent>, MessagePosition, CancellationToken, IEnumerable<EventHubEvent<TEvent>>> readEventsFromPosition, // CaptureProcessor.readEventsFromPosition
        Func<PartitionID, CancellationToken, Task<TState>> loadLastState,                                                             // MeterCollectionStore.loadLastState
        Func<TState, StartingPosition> determinePosition,                                                                             // MeterCollectionLogic.getEventPosition
        CancellationToken cancellationToken = default)
    {
        Task<TState> determineInitialState(PartitionInitializingEventArgs args, CancellationToken ct) => 
            loadLastState(PartitionID.create(args.PartitionId), ct);
        
        return CreateInternal(logger, 
                determineInitialState, 
                determinePosition,
                newEventProcessorClient,
                newEventHubConsumerClient,
                eventDataToEvent,
                createEventHubEventFromEventData,
                readAllEvents,
                readEventsFromPosition,
                cancellationToken)
            .GroupBy(getPartitionId);
    }
}