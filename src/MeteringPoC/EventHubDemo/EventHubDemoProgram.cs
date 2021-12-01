using System.Reactive.Linq;
using System.Reflection;
using Metering.Types;
using Metering.Types.EventHub;
using SomeMeterCollection = Microsoft.FSharp.Core.FSharpOption<Metering.Types.MeterCollection>;

// https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Azure.Messaging.EventHubs/samples/Sample05_ReadingEvents.md

Console.Title = Assembly.GetExecutingAssembly().GetName().Name;

using CancellationTokenSource cts = new();

MeteringConnections connections = MeteringConnectionsModule.getFromEnvironment();

Console.WriteLine($"Reading from {connections.EventProcessorClient.FullyQualifiedNamespace}");

var groupedByPartitionId = Metering.EventHubObservableClient.create(connections, cts.Token);

MeteringConfigurationProvider meteringConfig = 
    MeteringConfigurationProviderModule.create(
        config: connections,
        marketplaceClient: MarketplaceClient.submitCsharp.ToFSharpFunc());

Func<SomeMeterCollection, EventHubProcessorEvent<SomeMeterCollection, MeteringUpdateEvent>, SomeMeterCollection> accumulator = 
    MeteringAggregator.createAggregator(meteringConfig);

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
    Console.WriteLine($"event: {partitionId.value()}: {Json.toStr(0, meterCollection)}");
    //MeterCollectionStore.storeLastState(
    //    snapshotContainerClient: config.SnapshotStorage,
    //    meterCollection: x).Wait();
};

await Console.Out.WriteLineAsync("Press <Return> to close...");
_ = await Console.In.ReadLineAsync();

subscriptions.ForEach(subscription => subscription.Dispose());
cts.Cancel();
