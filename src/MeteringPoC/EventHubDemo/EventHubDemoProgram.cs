using System.Reactive.Linq;
using System.Reflection;
using Metering.ClientSDK;
using Metering.Types;
using Metering.Types.EventHub;
using SomeMeterCollection = Microsoft.FSharp.Core.FSharpOption<Metering.Types.MeterCollection>;
using System.Collections.Concurrent;

// https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Azure.Messaging.EventHubs/samples/Sample05_ReadingEvents.md

Console.Title = Assembly.GetExecutingAssembly().GetName().Name;

//var start = DateTime.Now;
//string elapsed() => DateTime.Now.Subtract(start).ToString();
//using AzureEventSourceListener listener = new(level: EventLevel.LogAlways, log: (e, message) => {
//    if (e.EventSource.Name.StartsWith("Azure-Messaging-EventHubs")) {
//        Console.WriteLine($"{elapsed()} {e.EventName.W(45)} {e.Level.ToString().W(10)} {message}"); } });

using CancellationTokenSource cts = new();

MeteringConnections connections = MeteringConnectionsModule.getFromEnvironment();

MeteringConfigurationProvider config =
    MeteringConfigurationProviderModule.create(
        connections: connections,
        marketplaceClient: MarketplaceClient.submitUsagesCsharp.ToFSharpFunc());

Console.WriteLine($"Reading from {connections.EventHubConfig.EventHubName.FullyQualifiedNamespace}");

Func<SomeMeterCollection, EventHubProcessorEvent<SomeMeterCollection, MeteringUpdateEvent>, SomeMeterCollection> accumulator = 
    MeteringAggregator.createAggregator(config);

//var items = new ConcurrentDictionary<
List<IDisposable> subscriptions = new();

var groupedSub = Metering.EventHubObservableClient.create(config, cts.Token).Subscribe(onNext: group => {
    var partitionId = group.Key;
    Console.WriteLine($"New group: {partitionId.value()}");
    var events = group
        .Scan(
            seed: MeterCollectionModule.Uninitialized,
            accumulator: accumulator
        )
        .Choose() // '.Choose()' is cleaner than '.Where(x => x.IsSome()).Select(x => x.Value)'
        //.StartWith()
        //.PublishLast()
        ;

    IDisposable subscription = events
        .Subscribe(
            onNext: coll => handleCollection(config, partitionId, coll),
            onError: ex => { 
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error {partitionId.value()}: {ex.Message}"); 
                Console.ResetColor();
            },
            onCompleted: () =>
            {   
                Console.ForegroundColor = ConsoleColor.Blue; 
                Console.WriteLine($"Closing {partitionId.value()}");
                Console.ResetColor();
            });
    subscriptions.Add(subscription);

    IDisposable subscription2 = SubscribeEmitter(events, config);
    subscriptions.Add(subscription2);
});
subscriptions.Add(groupedSub);

static IDisposable SubscribeEmitter(IObservable<MeterCollection> events, MeteringConfigurationProvider config)
{
    List<MarketplaceRequest> previousToBeSubmitted = new();
    ConcurrentQueue<MarketplaceRequest[]> tobeSubmitted = new();
    var producer = config.MeteringConnections.createEventHubProducerClient();

    var task = Task.Factory.StartNew(async () => {
        while (true)
        {
            await Task.Delay(1000);
            if (tobeSubmitted.TryDequeue(out var usage))
            {
                var response = await config.SubmitUsage(usage);
                await producer.ReportUsagesSubmitted(response, CancellationToken.None);

                await Console.Out.WriteLineAsync($"XXXXXXXXXXX Got another {response.Results.Length} requests");
            }
        }
    });

    return events
        .Subscribe(
            onNext: meterCollection =>
            {
                var current = meterCollection.metersToBeSubmitted().ToList();
                var newOnes = current.Except(previousToBeSubmitted).ToList();
                if (newOnes.Any())
                {
                    Console.Out.WriteLineAsync($"???????????????????? Enqueueing {newOnes.Count} requests to be handled");
                    newOnes
                        .Chunk(25)
                        .ForEach(tobeSubmitted.Enqueue);
                }
                previousToBeSubmitted = current;
            }
        );
}

static void handleCollection (MeteringConfigurationProvider config, PartitionID partitionId, MeterCollection meterCollection) {
    //Console.WriteLine($"partition-{partitionId.value()}: {meterCollection.getLastUpdateAsString()} {Json.toStr(0, meterCollection).UpTo(30)}");

    //Console.WriteLine(MeterCollectionModule.toStr(meterCollection));
    // Console.WriteLine(meterCollection.getLastSequenceNumber());

    if (meterCollection.getLastSequenceNumber() % 100 == 0)
    {
        Console.WriteLine($"Processed event {meterCollection.getLastSequenceNumber()}");
    }

    // Console.WriteLine(Json.toStr(2, meterCollection));
    if (meterCollection.getLastSequenceNumber() % 1000 == 0)
    {
        MeterCollectionStore.storeLastState(config, meterCollection: meterCollection).Wait();
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
    public static string UpTo(this string s, int length) =>  s.Length > length ? s[..length] : s;
    public static string W(this string s, int width) => String.Format($"{{0,-{width}}}", s);

    public static void ForEach<T>(this IEnumerable<T> ts, Action<T> action)
    {
        foreach (var t in ts)
            action(t);
    }
}