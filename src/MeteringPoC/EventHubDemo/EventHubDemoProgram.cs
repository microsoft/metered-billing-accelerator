using System.Diagnostics.Tracing;
using System.Reactive.Linq;
using System.Reflection;
using Azure.Core.Diagnostics;
using Metering.Types;
using Metering.Types.EventHub;
using SomeMeterCollection = Microsoft.FSharp.Core.FSharpOption<Metering.Types.MeterCollection>;

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
        marketplaceClient: MarketplaceClient.submitCsharp.ToFSharpFunc());

Console.WriteLine($"Reading from {connections.EventProcessorClient.FullyQualifiedNamespace}");

var groupedByPartitionId = Metering.EventHubObservableClient.create(config, cts.Token);

Func<SomeMeterCollection, EventHubProcessorEvent<SomeMeterCollection, MeteringUpdateEvent>, SomeMeterCollection> accumulator = 
    MeteringAggregator.createAggregator(config);

List<IDisposable> subscriptions = new();

var groupedSub = groupedByPartitionId.Subscribe(onNext: group => {
    var partitionId = group.Key;
    Console.WriteLine($"New group: {partitionId.value()}");
    IDisposable subscription = group
        .Scan(
            seed: MeterCollectionModule.Uninitialized,
            accumulator: accumulator
        )
        .Select(x => x.Value)
        //.StartWith()
        //.PublishLast()
        .Subscribe(
            onNext: coll => handleCollection(partitionId, coll),
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
});
subscriptions.Add(groupedSub);


void handleCollection (PartitionID partitionId, MeterCollection meterCollection) {
    Console.WriteLine($"partition-{partitionId.value()}: {meterCollection.getLastUpdate()} {Json.toStr(0, meterCollection).UpTo(30)}");
    MeterCollectionStore.storeLastState(config, meterCollection: meterCollection).Wait();
};

await Console.Out.WriteLineAsync("Press <Return> to close...");
_ = await Console.In.ReadLineAsync();

subscriptions.ForEach(subscription => subscription.Dispose());
cts.Cancel();

public static class E
{
    public static string UpTo(this string s, int length) =>  s.Length > length ? s[..length] : s;
    public static string W(this string s, int width) => String.Format($"{{0,-{width}}}", s);
}