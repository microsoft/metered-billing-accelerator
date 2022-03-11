// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Runtime.InteropServices;
    using System.Reactive.Disposables;
    using System.Reactive.Linq;
    using Microsoft.Extensions.Logging;
    using Azure.Messaging.EventHubs;
    using Azure.Messaging.EventHubs.Processor;
    using Azure.Messaging.EventHubs.Consumer;
    using Metering.BaseTypes;
    using Metering.BaseTypes.EventHub;
    using Microsoft.FSharp.Core;
    using System.Data;

    public static class EventHubObservableClientCSharp
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
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var innerCancellationToken = cts.Token;
                registerCancellationMessage(innerCancellationToken, "innerCancellationToken is Cancelled");

                Task ProcessEvent(ProcessEventArgs processEventArgs)
                {
                    try
                    {
                        FSharpOption<EventHubEvent<TEvent>> x = createEventHubEventFromEventData(eventDataToEvent, processEventArgs);
                        if (x.IsSome())
                        {
                            o.OnNext(EventHubProcessorEvent<TState, TEvent>.NewEventHubEvent(x.Value));
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

              throw new NotImplementedException();
            }

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
                loadLastState(PartitionIDModule.create(args.PartitionId), ct);
            
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
}