using System.Reactive.Linq;
using System.Reflection;
using Azure.Messaging.EventHubs;
using Metering.Types;
using Metering.Types.EventHub;
using SomeMeterCollection = Microsoft.FSharp.Core.FSharpOption<Metering.Types.MeterCollection>;
using MarketplaceSubmissionResult = Microsoft.FSharp.Core.FSharpResult<Metering.Types.MarketplaceSuccessResponse, Metering.Types.MarketplaceSubmissionError>;
using static Metering.Types.InternalResourceId;

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

var meters = Enumerable
    .Range(5, 15)
    .Select(hour => MeteringDateTimeModule.create(2021, 12, 16, hour, 00, 00))
    .Select(effectiveStartTime => new MarketplaceRequest(
        effectiveStartTime: effectiveStartTime,
        planId: PlanIdModule.create("free_monthly_yearly"),
        dimensionId: DimensionIdModule.create("datasourcecharge"),
        quantity: QuantityModule.createInt(100),
        resourceId: SaaSSubscription.NewSaaSSubscription(SaaSSubscriptionIDModule.create("8151a707-467c-4105-df0b-44c3fca5880d"))))
    .ToList();

var result = config.SubmitUsage(meters).Result.Results;

Console.WriteLine(result);
//foreach (var state in await config.fetchStates())
//{
//    Console.WriteLine($"Partition {state.Key}");
//    Console.WriteLine(MeterCollectionModule.toStr(state.Value));
//}

//Console.ReadLine();
//// Show how late we are
//while (true)
//{
//    var status = await config.fetchEventsToCatchup();
//    Console.WriteLine(status);
//    await Task.Delay(TimeSpan.FromSeconds(1.2));
//}

Console.WriteLine($"Reading from {connections.EventHubConfig.EventHubName.FullyQualifiedNamespace}");

Func<SomeMeterCollection, EventHubProcessorEvent<SomeMeterCollection, MeteringUpdateEvent>, SomeMeterCollection> accumulator = 
    MeteringAggregator.createAggregator(config);

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

static void handleCollection (PartitionID partitionId, MeterCollection meterCollection) {
    //Console.WriteLine($"partition-{partitionId.value()}: {meterCollection.getLastUpdateAsString()} {Json.toStr(0, meterCollection).UpTo(30)}");
    
    //Console.WriteLine(MeterCollectionModule.toStr(meterCollection));
    Console.WriteLine(meterCollection.getLastSequenceNumber());

    // Console.WriteLine(Json.toStr(2, meterCollection));
    //if (meterCollection.getLastSequenceNumber() % 100 == 0)
    {
        //MeterCollectionStore.storeLastState(config, meterCollection: meterCollection).Wait();
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
}