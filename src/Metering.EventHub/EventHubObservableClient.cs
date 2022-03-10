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
            Func<EventData, TEvent> eventDataToEvent,                                                                                     // CaptureProcessor.toMeteringUpdateEvent
            Func<EventProcessorClient> newEventProcessorClient,                                                                           // config.MeteringConnections.createEventProcessorClient
            Func<EventHubConsumerClient> newEventHubConsumerClient,                                                                       // config.MeteringConnections.createEventHubConsumerClient
            Func<Func<EventData, TEvent>, ProcessEventArgs, FSharpOption<EventHubEvent<TEvent>>> createEventHubEventFromEventData,        // EventHubIntegration.createEventHubEventFromEventData
            Func<Func<EventData, TEvent>, PartitionID, CancellationToken, IEnumerable<EventHubEvent<TEvent>>> readAllEvents,              // CaptureProcessor.readAllEvents
            Func<Func<EventData, TEvent>, MessagePosition, CancellationToken, IEnumerable<EventHubEvent<TEvent>>> readEventsFromPosition, // CaptureProcessor.readEventsFromPosition
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
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
                    eventDataToEvent,
                    newEventProcessorClient,
                    newEventHubConsumerClient,
                    createEventHubEventFromEventData,
                    readAllEvents,
                    readEventsFromPosition,
                    cancellationToken)
                .GroupBy(getPartitionId);
        }
    }
}