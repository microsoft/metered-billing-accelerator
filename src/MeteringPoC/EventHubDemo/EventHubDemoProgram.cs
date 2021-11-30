using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Processor;
using Metering.Messaging;
using Metering.Types;
using Metering.Types.EventHub;
using System.Reactive.Linq;
using System.Reflection;
using SomeMeterCollection = Microsoft.FSharp.Core.FSharpOption<Metering.Types.MeterCollection>;

// https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Azure.Messaging.EventHubs/samples/Sample05_ReadingEvents.md

Console.Title = Assembly.GetExecutingAssembly().GetName().Name;

var config = MeteringConnectionsModule.getFromEnvironment(
    consumerGroupName: EventHubConsumerClient.DefaultConsumerGroupName);

var meteringConfig = MeteringConfigurationProviderModule.create(config: config,
    marketplaceClient: MarketplaceClient.submitCsharp.ToFSharpFunc());

Console.WriteLine($"Reading from {config.EventProcessorClient.FullyQualifiedNamespace}");

Task<SomeMeterCollection> determineInitialState (PartitionInitializingEventArgs arg, CancellationToken ct) => 
    MeterCollectionStore.loadLastState(
        snapshotContainerClient: config.SnapshotStorage,
        partitionID: PartitionIDModule.create(arg.PartitionId),
        cancellationToken: ct);

using CancellationTokenSource cts = new();

IObservable<IGroupedObservable<PartitionID, EventHubProcessorEvent<SomeMeterCollection, MeteringUpdateEvent>>> groupedByPartitionId = 
    config.EventProcessorClient
        .CreateEventHubProcessorEventObservable<SomeMeterCollection, MeteringUpdateEvent>(
            determineInitialState: determineInitialState,
            determinePosition: MeterCollectionModule.getEventPosition,
            converter: x => Json.fromStr<MeteringUpdateEvent>(x.EventBody.ToString()),
            cancellationToken: cts.Token)
        .GroupBy(EventHubProcessorEvent.partitionId);

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
        .Subscribe(onNext: coll => handleCollection(partitionId, coll));
    
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
