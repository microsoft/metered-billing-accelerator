// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Aggregator
{
    using System.Reactive.Linq;
    using System.Collections.Concurrent;
    using Metering.BaseTypes;
    using Metering.BaseTypes.EventHub;
    using Metering.Integration;
    using Metering.ClientSDK;
    using SomeMeterCollection = Microsoft.FSharp.Core.FSharpOption<Metering.BaseTypes.MeterCollection>;
    using Microsoft.FSharp.Core;
    using Metering.EventHub;
    using Azure.Messaging.EventHubs;
    using Azure.Messaging.EventHubs.Processor;

    // https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Azure.Messaging.EventHubs/samples/Sample05_ReadingEvents.md

    public class AggregatorWorker : BackgroundService
    {
        private readonly ILogger<AggregatorWorker> _logger;
        private readonly MeteringConfigurationProvider config;

        public AggregatorWorker(ILogger<AggregatorWorker> logger, MeteringConfigurationProvider mcp)
        {
            (_logger, config) = (logger, mcp);
        }

        private IDisposable SubscribeEmitter(IObservable<MeterCollection> events)
        {
            List<MarketplaceRequest> previousToBeSubmitted = new();
            ConcurrentQueue<MarketplaceRequest[]> tobeSubmitted = new();
            var producer = config.MeteringConnections.createEventHubProducerClient();

            // Run an endless loop,
            // - to look at the concurrent queue,
            // - submit REST calls to marketplace, and then
            // - submit the marketplace responses to EventHub. 
            var task = Task.Factory.StartNew(async () => {
                while (true)
                {
                    await Task.Delay(1000);
                    if (tobeSubmitted.TryDequeue(out var usage))
                    {
                        var response = await config.SubmitUsage(usage);
                        await producer.ReportUsagesSubmitted(response, CancellationToken.None);
                        _logger.Log(LogLevel.Information, "Submitted {0} values", response.Results.Length);
                    }
                }
            });

            return events
                .Subscribe(
                    onNext: meterCollection =>
                    {
                        // Only add new (unseen) events to the concurrent queue.
                        var current = meterCollection.metersToBeSubmitted().ToList();
                        var newOnes = current.Except(previousToBeSubmitted).ToList();
                        if (newOnes.Any())
                        {
                            newOnes
                                .Chunk(25)
                                .ForEach(tobeSubmitted.Enqueue);
                        }
                        previousToBeSubmitted = current;
                    }
                );
        }

        private void RegularlyCreateSnapshots(PartitionID partitionId, MeterCollection meterCollection, Func<string> prefix)
        {
            if (meterCollection.getLastSequenceNumber() % 100 == 0)
            {
                _logger.LogInformation($"{prefix()} Processed event {partitionId.value()}#{meterCollection.getLastSequenceNumber()}");
            }

            //if (meterCollection.getLastSequenceNumber() % 500 == 0)
            {
                MeterCollectionStore.storeLastState(config, meterCollection: meterCollection).Wait();
                _logger.LogInformation($"{prefix()} Saved state {partitionId.value()}#{meterCollection.getLastSequenceNumber()}");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Func<SomeMeterCollection, EventHubProcessorEvent<SomeMeterCollection, MeteringUpdateEvent>, SomeMeterCollection> accumulator =
                MeteringAggregator.createAggregator(config.TimeHandlingConfiguration);

            List<IDisposable> subscriptions = new();

            // pretty-print which partitions we already 'own'
            var props = await config.MeteringConnections.createEventHubConsumerClient().GetEventHubPropertiesAsync(stoppingToken);
            var partitions = new string[props.PartitionIds.Length];
            Array.Fill(partitions, "_");
            string currentPartitions() => string.Join("", partitions);

            var dummy = EventHubObservableClientCSharp.Create(
                logger: _logger,
                getPartitionId: EventHubIntegration.partitionId,
                newEventProcessorClient: config.MeteringConnections.createEventProcessorClient,
                newEventHubConsumerClient: config.MeteringConnections.createEventHubConsumerClient,
                eventDataToEvent: CaptureProcessor.toMeteringUpdateEvent,
                createEventHubEventFromEventData: EventHubIntegration.CreateEventHubEventFromEventData,
                readAllEvents: config.MeteringConnections.ReadAllEvents,
                readEventsFromPosition: config.MeteringConnections.ReadEventsFromPosition,
                loadLastState: config.loadLastState,
                determinePosition: MeterCollectionLogic.getEventPosition,
                cancellationToken: stoppingToken);

            var groupedSub = EventHubObservableClient
                .create<SomeMeterCollection, MeteringUpdateEvent>(
                    logger: _logger,
                    getPartitionId: FuncConvert.FromFunc<EventHubProcessorEvent<SomeMeterCollection, MeteringUpdateEvent>, PartitionID>(EventHubIntegration.partitionId),
                    newEventProcessorClient: FuncConvert.FromFunc(config.MeteringConnections.createEventProcessorClient),
                    newEventHubConsumerClient: FuncConvert.FromFunc(config.MeteringConnections.createEventHubConsumerClient),
                    eventDataToEvent: FuncConvert.FromFunc<EventData, MeteringUpdateEvent>(CaptureProcessor.toMeteringUpdateEvent),
                    createEventHubEventFromEventData: FuncConvert.FromFunc<FSharpFunc<EventData, MeteringUpdateEvent>, ProcessEventArgs, FSharpOption<EventHubEvent<MeteringUpdateEvent>>>(EventHubIntegration.createEventHubEventFromEventData),
                    readAllEvents: FuncConvert.FromFunc<FSharpFunc<EventData, MeteringUpdateEvent>, PartitionID, CancellationToken, IEnumerable<EventHubEvent<MeteringUpdateEvent>>>((a, b, c) => CaptureProcessor.readAllEvents(a, b, c, config.MeteringConnections)),
                    readEventsFromPosition: FuncConvert.FromFunc<FSharpFunc<EventData, MeteringUpdateEvent>, MessagePosition, CancellationToken, IEnumerable<EventHubEvent<MeteringUpdateEvent>>>((a, b, c) => CaptureProcessor.readEventsFromPosition(a, b, c, config.MeteringConnections)),
                    loadLastState: FuncConvert.FromFunc<PartitionID, CancellationToken, Task<SomeMeterCollection>>(config.loadLastState),
                    determinePosition: FuncConvert.FromFunc<SomeMeterCollection, StartingPosition>(MeterCollectionLogic.getEventPosition),
                    cancellationToken: stoppingToken)
                .Subscribe(
                    onNext: group => {
                        var partitionId = group.Key;
                        partitions[int.Parse(partitionId.value())] = partitionId.value();

                        IObservable<MeterCollection> events = group
                            .Scan(seed: MeterCollectionModule.Uninitialized, accumulator: accumulator)
                            .Choose(); // '.Choose()' is cleaner than '.Where(x => x.IsSome()).Select(x => x.Value)'

                        // Subscribe the creation of snapshots
                        events
                            .Subscribe(
                                onNext: coll => RegularlyCreateSnapshots(partitionId, coll, currentPartitions),
                                onError: ex =>
                                {
                                    _logger.LogError($"Error {partitionId.value()}: {ex.Message}");
                                },
                                onCompleted: () =>
                                {
                                    _logger.LogWarning($"Closing {partitionId.value()}");
                                    partitions[int.Parse(partitionId.value())] = "_";
                                })
                            .AddToSubscriptions(subscriptions);

                        // Subscribe the submission to marketplace.
                        SubscribeEmitter(events)
                            .AddToSubscriptions(subscriptions);
                    },
                    onCompleted: () => {
                        _logger.LogWarning($"Closing everything");
                    }, 
                    onError: ex => {
                        _logger.LogCritical($"Error: {ex.Message}");
                    }
                );
            subscriptions.Add(groupedSub);

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }

            subscriptions.ForEach(subscription => subscription.Dispose());
        }
    }

    internal static class E
    {
        public static void AddToSubscriptions(this IDisposable i, List<IDisposable> l) => l.Add(i);
        public static string UpTo(this string s, int length) => s.Length > length ? s[..length] : s;
        public static string W(this string s, int width) => String.Format($"{{0,-{width}}}", s);
        public static void ForEach<T>(this IEnumerable<T> ts, Action<T> action) { foreach (var t in ts) { action(t); } }
    }
}