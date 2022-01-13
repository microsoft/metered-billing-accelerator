using System.Reactive.Linq;
using System.Reflection;
using Metering.ClientSDK;
using Metering.Types;
using Metering.Types.EventHub;
using SomeMeterCollection = Microsoft.FSharp.Core.FSharpOption<Metering.Types.MeterCollection>;
using System.Collections.Concurrent;
using Azure.Messaging.EventHubs;

// https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Azure.Messaging.EventHubs/samples/Sample05_ReadingEvents.md

Console.Title = Assembly.GetExecutingAssembly().GetName().Name;

using CancellationTokenSource cts = new();

MeteringConnections connections = MeteringConnectionsModule.getFromEnvironment();

MeteringConfigurationProvider config =
    MeteringConfigurationProviderModule.create(
        connections: connections,
        marketplaceClient: MarketplaceClient.submitUsagesCsharp.ToFSharpFunc());

Console.WriteLine($"Reading from {connections.EventHubConfig.EventHubName.FullyQualifiedNamespace}");

Func<SomeMeterCollection, EventHubProcessorEvent<SomeMeterCollection, MeteringUpdateEvent>, SomeMeterCollection> accumulator = 
    MeteringAggregator.createAggregator(config);

List<IDisposable> subscriptions = new();

// pretty-print which partitions we already 'own'
var props = await config.MeteringConnections.createEventHubConsumerClient().GetEventHubPropertiesAsync();
var partitions = new string[props.PartitionIds.Length];
Array.Fill(partitions, "_");
Func<string> currentPartitions = () => string.Join("", partitions); 

var groupedSub = Metering.EventHubObservableClient.create(config, cts.Token).Subscribe(onNext: group => {
    var partitionId = group.Key;
    partitions[int.Parse(partitionId.value())] = partitionId.value();

    IObservable<MeterCollection> events = group
        .Scan(seed: MeterCollectionModule.Uninitialized, accumulator: accumulator)
        .Choose(); // '.Choose()' is cleaner than '.Where(x => x.IsSome()).Select(x => x.Value)'

    // Subscribe the creation of snapshots
    events
        .Subscribe(
            onNext: coll => RegularlyCreateSnapshots(config, partitionId, coll, currentPartitions),
            onError: ex =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error {partitionId.value()}: {ex.Message}");
                Console.ResetColor();
            },
            onCompleted: () =>
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"Closing {partitionId.value()}");
                Console.ResetColor();
                partitions[int.Parse(partitionId.value())] = "_";
            })
        .AddToSubscriptions(subscriptions);

    // Subscribe the submission to marketplace.
    SubscribeEmitter(events, config)
        .AddToSubscriptions(subscriptions);
});
subscriptions.Add(groupedSub);

static IDisposable SubscribeEmitter(IObservable<MeterCollection> events, MeteringConfigurationProvider config)
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

static void RegularlyCreateSnapshots(MeteringConfigurationProvider config, PartitionID partitionId, MeterCollection meterCollection, Func<string> prefix) {
    //Console.WriteLine($"partition-{partitionId.value()}: {meterCollection.getLastUpdateAsString()} {Json.toStr(0, meterCollection).UpTo(30)}");
    //Console.WriteLine(MeterCollectionModule.toStr(meterCollection));
    //Console.WriteLine(meterCollection.getLastSequenceNumber());

    if (meterCollection.getLastSequenceNumber() % 100 == 0)
    {
        Console.WriteLine($"{prefix()} Processed event {partitionId.value()}#{meterCollection.getLastSequenceNumber()}");
    }

    if (meterCollection.getLastSequenceNumber() % 500 == 0)
    {
        MeterCollectionStore.storeLastState(config, meterCollection: meterCollection).Wait();
        Console.WriteLine($"{prefix()} Saved state {partitionId.value()}#{meterCollection.getLastSequenceNumber()}");
    }
};

await Console.Out.WriteLineAsync("Press <Return> to close...");
_ = await Console.In.ReadLineAsync();

subscriptions.ForEach(subscription => subscription.Dispose());
cts.Cancel();

#pragma warning disable CA1050 // Declare types in namespaces
public static class E
#pragma warning restore CA1050 // Declare types in namespaces
{
    public static void AddToSubscriptions(this IDisposable i, List<IDisposable> l) => l.Add(i);
    public static string UpTo(this string s, int length) =>  s.Length > length ? s[..length] : s;
    public static string W(this string s, int width) => String.Format($"{{0,-{width}}}", s);
    public static void ForEach<T>(this IEnumerable<T> ts, Action<T> action) { foreach (var t in ts) { action(t); } }
}