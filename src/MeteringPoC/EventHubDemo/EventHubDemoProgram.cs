using System.Reflection;
using System.Reactive.Linq;
using Azure.Messaging.EventHubs.Consumer;
using Metering;
using Metering.Messaging;
using Metering.Types;
using Metering.Types.EventHub;
using SomeMeterCollection = Microsoft.FSharp.Core.FSharpOption<Metering.Types.MeterCollection>;
using Microsoft.Extensions.Configuration;

// https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Azure.Messaging.EventHubs/samples/Sample05_ReadingEvents.md

Console.Title = Assembly.GetExecutingAssembly().GetName().Name;

var builder = new ConfigurationBuilder().AddEnvironmentVariables(prefix: "AZURE_METERING_");
var configuration = builder.Build();

string MARKETPLACE_CLIENT_ID = configuration["MARKETPLACE_CLIENT_ID"];
string MARKETPLACE_CLIENT_SECRET = configuration["MARKETPLACE_CLIENT_SECRET"];
string MARKETPLACE_TENANT_ID = configuration["MARKETPLACE_TENANT_ID"];

var config = DemoCredentialModule.get(consumerGroupName: EventHubConsumerClient.DefaultConsumerGroupName);




var meteringConfig = MeteringConfigurationProviderModule.create(
    meteringApiCreds: config.MeteringAPICredentials,
    marketplaceClient: MarketplaceClient.submitCsharp.ToFSharpFunc(),
    snapshotStorage: config.GetSnapshotStorage());

var processor = config.CreateEventHubProcessorClient();
//var consumerClient = config.CreateEventHubConsumerClient();

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

groupedByPartitionId.Subscribe(onNext: group => {
    var partitionId = PartitionIDModule.value(group.Key);
    Console.WriteLine($"New group: {partitionId}");

    var accumulator = MeteringAggregator.createAggregator(meteringConfig);
    group
        .Scan(
            seed: MeterCollectionModule.Uninitialized,
            accumulator: accumulator
        ).Subscribe(onNext: x => {
            Console.WriteLine($"event: {partitionId}: {Json.toStr(0, x.Value)}");
            MeterCollectionStore.storeLastState(
                snapshotContainerClient: config.GetSnapshotStorage(),
                meterCollection: x.Value).Wait();
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
