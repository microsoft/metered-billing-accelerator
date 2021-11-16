using System.Reflection;
using System.Reactive.Linq;
using Azure.Messaging.EventHubs.Consumer;
using Metering;
using Metering.Messaging;
using Metering.Types;
using Metering.Types.EventHub;
using SomeMeterCollection = Microsoft.FSharp.Core.FSharpOption<Metering.Types.MeterCollection>;

// https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Azure.Messaging.EventHubs/samples/Sample05_ReadingEvents.md

Console.Title = Assembly.GetExecutingAssembly().GetName().Name;

var config = DemoCredentials.Get(
    consumerGroupName: EventHubConsumerClient.DefaultConsumerGroupName);
var processor = config.CreateEventHubProcessorClient();
var consumerClient = config.CreateEventHubConsumerClient();

Console.WriteLine(config.EventHubInformation.EventHubNamespaceName);

using CancellationTokenSource cts = new();

var groupedByPartitionId = processor
    .CreateEventHubProcessorEventObservable<SomeMeterCollection, MeteringUpdateEvent>(
        determineInitialState: (arg, ct) => MeterCollectionStore.loadLastState(
            snapshotContainerClient: config.GetSnapshotStorage(),
            partitionID: PartitionIDModule.create(arg.PartitionId),
            cancellationToken: ct),
        determinePosition: MeterCollectionModule.getEventPosition,
        converter: x => Json.fromStr<MeteringUpdateEvent>(x.EventBody.ToString()),
        cancellationToken: cts.Token)
    .GroupBy(EventHubProcessorEvent.partitionId);

var meteringConfig = MeteringConfigurationProviderModule.Dummy;

groupedByPartitionId.Subscribe(onNext: group => {
    var partitionId = PartitionIDModule.value(group.Key);
    Console.WriteLine($"New group: {partitionId}");

    var accumulator = MeteringAggregator.createAggregator(meteringConfig);
    group
        .Scan(
            seed: MeterCollectionModule.Uninitialized,
            accumulator: accumulator
        ).Subscribe(onNext: x => {
            
            Console.WriteLine($"event: {partitionId}: {Json.toStr(0,x.Value)}");
        });
});
 
//processorEvents.Subscribe(onNext: e => {
//    Func<MeteringUpdateEvent, string> conv = e => $"partitionKey {MeteringUpdateEventModule.partitionKey(e)} - {e.GetType()}";
//    var str = EventHubProcessorEvent.toStr<SomeMeterCollection, MeteringUpdateEvent>(converter: conv.ToFSharpFunc(), e);
//    Console.WriteLine(str);
//});

//IObservable<(MeteringEvent, EventsToCatchup)> observable2 = consumerClient.CreateAggregatorObservable(
//        someMessagePosition: FSharpOption<MessagePosition>.None,
//        cancellationToken: cts.Token);

//observable2.Subscribe(onNext: x => {
//    var (e, d) = x;
//    Console.WriteLine($"{e} {x}");
//});


await Console.Out.WriteLineAsync("Press <Return> to close...");
_ = await Console.In.ReadLineAsync();
cts.Cancel();
