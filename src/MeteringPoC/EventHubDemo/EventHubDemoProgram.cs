using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Processor;
using Metering;
using Metering.Messaging;
using Metering.Types;
using Metering.Types.EventHub;
using System.Reactive.Linq;
using System.Reflection;
using SomeMeterCollection = Microsoft.FSharp.Core.FSharpOption<Metering.Types.MeterCollection>;

// https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Azure.Messaging.EventHubs/samples/Sample05_ReadingEvents.md

Console.Title = Assembly.GetExecutingAssembly().GetName().Name;

MeteringConnections connections = MeteringConnectionsModule.getFromEnvironment(
    consumerGroupName: EventHubConsumerClient.DefaultConsumerGroupName);


Console.WriteLine($"Reading from {connections.EventProcessorClient.FullyQualifiedNamespace}");

using CancellationTokenSource cts = new();


var groupedByPartitionId = Metering.EventHubObservableClient
    .create(connections, cts.Token);

//Task<SomeMeterCollection> determineInitialState (PartitionInitializingEventArgs arg, CancellationToken ct) => 
//    MeterCollectionStore.loadLastState(
//        snapshotContainerClient: connections.SnapshotStorage,
//        partitionID: PartitionIDModule.create(arg.PartitionId),
//        cancellationToken: ct);
//IObservable<IGroupedObservable<PartitionID, EventHubProcessorEvent<SomeMeterCollection, MeteringUpdateEvent>>> groupedByPartitionId =
//    connections.EventProcessorClient
//        .CreateEventHubProcessorEventObservable<SomeMeterCollection, MeteringUpdateEvent>(
//            determineInitialState: determineInitialState,
//            determinePosition: MeterCollectionModule.getEventPosition,
//            converter: x => Json.fromStr<MeteringUpdateEvent>(x.EventBody.ToString()),
//            cancellationToken: cts.Token)
//        .GroupBy(EventHubProcessorEvent.partitionId);

//IObservable<IGroupedObservable<PartitionID, EventHubProcessorEvent<SomeMeterCollection, MeteringUpdateEvent>>> groupedByPartitionId =
//    config.EventProcessorClient
//        .CreateEventHubProcessorEventObservableFSharp<SomeMeterCollection, MeteringUpdateEvent>(
//            determineInitialState: FSharpFuncUtil.ToFSharpFunc<PartitionInitializingEventArgs, CancellationToken, Task<SomeMeterCollection>>(determineInitialState),
//            determinePosition: FSharpFuncUtil.ToFSharpFunc<SomeMeterCollection, EventPosition>(func: MeterCollectionModule.getEventPosition),
//            converter: FSharpFuncUtil.ToFSharpFunc<EventData, MeteringUpdateEvent>(x => Json.fromStr<MeteringUpdateEvent>(x.EventBody.ToString())),
//            cancellationToken: cts.Token)
//        .GroupBy(EventHubProcessorEvent.partitionId);

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
