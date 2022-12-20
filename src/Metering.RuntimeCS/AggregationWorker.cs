
namespace Metering.RuntimeCS;

using Metering.BaseTypes;
using Metering.BaseTypes.EventHub;
using Metering.ClientSDK;
using Metering.EventHub;
using Metering.Integration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reactive.Linq;
using SomeMeterCollection = Microsoft.FSharp.Core.FSharpOption<Metering.BaseTypes.MeterCollection>;

// https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Azure.Messaging.EventHubs/samples/Sample05_ReadingEvents.md

public class AggregationWorker
{
    record MarketplaceRequestAndPartitionId(MarketplaceRequest MarketplaceRequest, PartitionID PartitionID);
    record MarketplaceRequestsAndPartitionId(IEnumerable<MarketplaceRequest> MarketplaceRequests, PartitionID PartitionID);

    private readonly ILogger _logger;
    private readonly MeteringConfigurationProvider config;

    public AggregationWorker(ILogger logger, MeteringConfigurationProvider mcp)
    {
        (_logger, config) = (logger, mcp);
    }

    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<IDisposable> subscriptions = new();

        // pretty-print which partitions we already 'own'
        var props = await config.MeteringConnections.createEventHubConsumerClient().GetEventHubPropertiesAsync(stoppingToken);
        var partitions = new string[props.PartitionIds.Length];
        Array.Fill(partitions, "_");
        string currentPartitions() => string.Join("-", partitions);

        var groupedSub = EventHubObservableClient
            .Create<SomeMeterCollection, MeteringUpdateEvent>(
                logger: _logger,
                getPartitionId: EventHubIntegration.partitionId,
                newEventProcessorClient: config.MeteringConnections.createEventProcessorClient,
                newEventHubConsumerClient: config.MeteringConnections.createEventHubConsumerClient,
                eventDataToEvent: CaptureProcessor.toMeteringUpdateEvent,
                createEventHubEventFromEventData: EventHubIntegration.CreateEventHubEventFromEventData,
                readAllEvents: config.MeteringConnections.ReadAllEvents,
                readEventsFromPosition: config.MeteringConnections.ReadEventsFromPosition,
                loadLastState: config.MeteringConnections.loadLastState,
                determinePosition: MeterCollectionLogic.getEventPosition,
                cancellationToken: stoppingToken)
            .Subscribe(
                onNext: group => {
                    var partitionId = group.Key;
                    partitions[int.Parse(partitionId.value)] = partitionId.value;

                    IObservable<MeterCollection> events = group
                        .Scan(seed: MeterCollection.Uninitialized, accumulator: MeteringAggregator.createAggregator)
                        .Choose( /* '.Choose()' is cleaner than '.Where(x => x.IsSome()).Select(x => x.Value)' */ );

                    // Subscribe the creation of snapshots
                    events
                        .Subscribe(
                            onNext: coll =>
                            {
                                RegularlyCreateSnapshots(partitionId, coll, currentPartitions);
                            },
                            onError: ex =>
                            {
                                _logger.LogError($"Error {partitionId.value}: {ex.Message}");
                            },
                            onCompleted: () =>
                            {
                                _logger.LogWarning($"Closing {partitionId.value}");
                                partitions[int.Parse(partitionId.value)] = "_";
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

    private IDisposable SubscribeEmitter(IObservable<MeterCollection> events)
    {
        List<MarketplaceRequestAndPartitionId> previousToBeSubmitted = new();
        ConcurrentQueue<MarketplaceRequestsAndPartitionId> tobeSubmitted = new();
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
                    var response = await config.SubmitUsage(usage.MarketplaceRequests);
                    await producer.ReportUsagesSubmitted(usage.PartitionID, response, CancellationToken.None);
                    _logger.Log(LogLevel.Information, "Submitted {0} values", response.Results.Length);
                }
            }
        });

        return events
            .Subscribe(
                onNext: meterCollection =>
                {
                    var partitionId = meterCollection.LastUpdate.Value.PartitionID;
                    // Only add new (unseen) events to the concurrent queue.
                    var current = meterCollection.metersToBeSubmitted.Select(request => new MarketplaceRequestAndPartitionId(request, partitionId)).ToList();

                    var newOnes = current.Except(previousToBeSubmitted).ToList();
                    if (newOnes.Any())
                    {
                        // Send batches to Azure Marketplace API which have resourceIds and resourceURIs which are tracked in the same event hub partition
                        newOnes
                            .GroupBy(g => g.PartitionID)
                            .SelectMany(g => g.Select(x => x.MarketplaceRequest))
                            .Chunk(25)
                            .Select(i => new MarketplaceRequestsAndPartitionId(i, partitionId))
                            .ForEach(tobeSubmitted.Enqueue);
                    }
                    previousToBeSubmitted = current;
                }
            );
    }

    private void RegularlyCreateSnapshots(PartitionID partitionId, MeterCollection meterCollection, Func<string> prefix)
    {
        //if (meterCollection.getLastSequenceNumber() % 500 == 0)
        {
            MeterCollectionStore.storeLastState(config.MeteringConnections, meterCollection: meterCollection).Wait();
            _logger.LogInformation($"{prefix()} Saved state {partitionId.value}#{meterCollection.getLastSequenceNumber()}");
        }
    }
}

public static class Extensions
{
    public static CancellationToken CancelAfter(this CancellationToken token, TimeSpan timeSpan) =>
        CancellationTokenSource.CreateLinkedTokenSource(
            token1: token,
            token2: new CancellationTokenSource(delay: timeSpan).Token).Token;

    public static void AddToSubscriptions(this IDisposable i, List<IDisposable> l) => l.Add(i);
    public static string UpTo(this string s, int length) => s.Length > length ? s[..length] : s;
    public static string W(this string s, int width) => String.Format($"{{0,-{width}}}", s);
    public static void ForEach<T>(this IEnumerable<T> ts, Action<T> action) { foreach (var t in ts) { action(t); } }
}
