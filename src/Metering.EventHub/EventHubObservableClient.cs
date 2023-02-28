// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Integration;

using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Processor;
using Azure.Messaging.EventHubs.Producer;
using Microsoft.Extensions.Logging;
using Microsoft.FSharp.Core;
using Metering.BaseTypes;
using Metering.BaseTypes.EventHub;
using MeteringDateTime = NodaTime.ZonedDateTime;
using SequenceNumber = System.Int64;
using PartitionID = Metering.BaseTypes.EventHub.PartitionID;

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
        Func<EventHubProducerClient> newEventHubProducerClient,                                                                       // config.MeteringConnections.createEventHubProducerClient
        Func<EventHubProducerClient, PingMessage, CancellationToken, Task> sendPing,
        Func<EventData, TEvent> eventDataToEvent,                                                                                     // CaptureProcessor.toMeteringUpdateEvent
        Func<Func<EventData, TEvent>, ProcessEventArgs, FSharpOption<EventHubEvent<TEvent>>> createEventHubEventFromEventData,        // EventHubIntegration.createEventHubEventFromEventData
        Func<Func<EventData, TEvent>, PartitionID, CancellationToken, IEnumerable<EventHubEvent<TEvent>>> readAllEvents,              // CaptureProcessor.readAllEvents
        Func<Func<EventData, TEvent>, MessagePosition, CancellationToken, IEnumerable<EventHubEvent<TEvent>>> readEventsFromPosition, // CaptureProcessor.readEventsFromPosition
        CancellationToken cancellationToken)
    {
        // Dictionary<PartitionID, EventHubProducerClient>
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

            EventHubProducerClient pingSender = newEventHubProducerClient();
            Dictionary<PartitionID, (CancellationTokenSource, Task)> pingTasks = new();

            Task SleepUntilNextHour(int minutesPastTheFullHour, CancellationToken ct)
            {
                var now = DateTime.Now;
                var oneHourFromNow = now.AddHours(1);
                var nextPing = new DateTime(year: oneHourFromNow.Year, month: oneHourFromNow.Month, day: oneHourFromNow.Day, hour: oneHourFromNow.Hour, minute: minutesPastTheFullHour, second: 0);
                var delta = nextPing.Subtract(now);
                return Task.Delay(delta, ct);
            }

            async Task SendTopOfHourPing(PartitionID partitionId, CancellationToken pingCancellationToken)
            {
                await sendPing(pingSender, PingMessageModule.create(partitionId, PingReason.ProcessingStarting), pingCancellationToken);
                while (!pingCancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Sleep until next hour, 10 mins after. 10 mins pat should be enough for all Event Hub partitions to be in the next hour.
                        await SleepUntilNextHour(minutesPastTheFullHour: 10, pingCancellationToken);

                        await sendPing(pingSender, PingMessageModule.create(partitionId, PingReason.TopOfHour), pingCancellationToken);
                        logger.LogInformation($"Sending a TopOfHour ping to {partitionId.value}");
                    }
                    catch (TaskCanceledException)
                    {
                        ;
                    }
                }
             }

            registerCancellationMessage(innerCancellationToken, "innerCancellationToken is Cancelled");

            async Task ProcessEvent(ProcessEventArgs processEventArgs)
            {
                var partitionId = processEventArgs.Partition.PartitionId;
                try
                {
                    FSharpOption<EventHubEvent<TEvent>> x = createEventHubEventFromEventData(eventDataToEvent, processEventArgs);
                    if (processEventArgs.HasEvent && x.IsSome())
                    {
                        var xValue = x.Value;

                        logger.LogInformation($"{MeteringDateTimeModule.toStr(xValue.MessagePosition.PartitionTimestamp)} Event {partitionId}#{xValue.MessagePosition.SequenceNumber} arrived");
                        o.OnNext(EventHubProcessorEvent<TState, TEvent>.NewEventReceived(xValue));
                    }
                    else
                    {
                        try
                        {
                            LastEnqueuedEventProperties catchUp = processEventArgs.Partition.ReadLastEnqueuedEventProperties();
                            logger.LogDebug($"Didn't find events: PartitionId {partitionId} SequenceNumber {catchUp.SequenceNumber} EnqueuedTime {catchUp.EnqueuedTime} LastReceivedTime {catchUp.LastReceivedTime}");
                        }
                        catch (InvalidOperationException)
                        {
                            await Task.Delay(millisecondsDelay: 1, cancellationToken: cancellationToken);
                            //await using EventHubConsumerClient consumerClient = newEventHubConsumerClient();
                            //var p = await consumerClient.GetEventHubPropertiesAsync(cancellationToken);
                            //PartitionProperties partitionProps = await consumerClient.GetPartitionPropertiesAsync(partitionId: partitionId, cancellationToken: cancellationToken);
                            //if (partitionProps.LastEnqueuedSequenceNumber > -1)
                            //{
                            //    var t = MeteringDateTime.FromDateTimeOffset(partitionProps.LastEnqueuedTime.ToUniversalTime());
                            //    logger.LogInformation($"Last message {partitionId}#{partitionProps.LastEnqueuedSequenceNumber} {MeteringDateTimeModule.toStr(t)}");
                            //}
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError($"Exception during ProcessEvent for partition {partitionId}: {ex.GetType().FullName}: {ex.Message}");
                }
            }

            Task ProcessError(ProcessErrorEventArgs processErrorEventArgs)
            {
                logger.LogError($"ProcessError: {processErrorEventArgs.Exception.Message}: {processErrorEventArgs.Exception.StackTrace}");
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

                // When we stop handling a certain partition, stop sending Ping messages into it.
                var partitionId = PartitionID.create(partitionClosingEventArgs.PartitionId);
                if (pingTasks.ContainsKey(partitionId))
                {
                    var (cts, pingTask) = pingTasks[partitionId];
                    pingTasks.Remove(partitionId);
                    cts.Cancel();
                }

                return Task.CompletedTask;
            }

            async Task PartitionInitializing(PartitionInitializingEventArgs partitionInitializingEventArgs)
            {
                logger.LogInformation($"PartitionInitializing: {partitionInitializingEventArgs.PartitionId}");
                var partitionIdStr = partitionInitializingEventArgs.PartitionId;
                var partitionId = PartitionID.create(partitionIdStr);

                var pingCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var pingCancellationToken = pingCancellationTokenSource.Token;
                Task pingTask = Task.Run(() => SendTopOfHourPing(partitionId, pingCancellationToken), pingCancellationToken);
                pingTasks.Add(partitionId, (pingCancellationTokenSource, pingTask));

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
                        EventHubConsumerClient consumerClient = newEventHubConsumerClient();

                        PartitionProperties partitionProps = await consumerClient.GetPartitionPropertiesAsync(
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
                    partitionInitializingEventArgs.DefaultStartingPosition = x.EventPosition;
                    var pid = PartitionID.create(partitionIdStr);
                    var ehpe = EventHubProcessorEvent<TState, TEvent>.NewPartitionInitializing(pid, initialState);
                    o.OnNext(ehpe);
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
                EventProcessorClient processor = newEventProcessorClient();
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
                    }
                    catch (TaskCanceledException) { }
                    catch (Exception ex) { o.OnError(ex); }
                    finally{
                        try
			{
				o.OnCompleted();
				processor.StopProcessing();
			}
			catch (Exception) { }
                    }
                }
                finally
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
        Func<EventHubProducerClient> newEventHubProducerClient,                                                                       // config.MeteringConnections.createEventHubProducerClient
        Func<EventHubProducerClient, PingMessage, CancellationToken, Task> sendPing,
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
                newEventHubProducerClient,
                sendPing,
                eventDataToEvent,
                createEventHubEventFromEventData,
                readAllEvents,
                readEventsFromPosition,
                cancellationToken)
            .GroupBy(getPartitionId);
    }
}
