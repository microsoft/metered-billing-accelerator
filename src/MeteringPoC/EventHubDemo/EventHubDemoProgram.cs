using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Processor;
using Metering;
using Metering.Messaging;
using Metering.Types;
using Metering.Types.EventHub;
using Microsoft.FSharp.Core;
using System.Reflection;

// https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Azure.Messaging.EventHubs/samples/Sample05_ReadingEvents.md

Console.Title = Assembly.GetExecutingAssembly().GetName().Name;

var config = DemoCredentials.Get(
    consumerGroupName: EventHubConsumerClient.DefaultConsumerGroupName);
var processor = config.CreateEventHubProcessorClient();
var consumerClient = config.CreateEventHubConsumerClient();

Console.WriteLine(config.EventHubInformation.EventHubNamespaceName);

async Task<(EventPosition, FSharpOption<MeterCollection>)> DetermineInitialState(PartitionInitializingEventArgs arg, CancellationToken cancellationToken = default)
{
    var someMeters = await MeterCollectionStore.loadLastState(
        snapshotContainerClient: config.GetSnapshotStorage(),
        partitionID: arg.PartitionId,
        cancellationToken: cancellationToken);

    var lastUpdate = MeterCollectionModule.lastUpdate(someMeters);
    var eventPosition = MessagePositionModule.startingPosition(lastUpdate);

    var message = MeterCollectionStore.isLoaded(someMeters)
        ? $"Loaded state for {arg.PartitionId}, starting {eventPosition}"
        : $"Could not load state for {arg.PartitionId}";
    Console.WriteLine(message);
    
    return (eventPosition, someMeters);
}

using CancellationTokenSource cts = new();

IObservable<EventHubProcessorEvent<MeteringUpdateEvent>> processorEvents = processor.CreateEventHubProcessorEventObservable(
    determineInitialState: DetermineInitialState,
    converter: x => Json.fromStr<MeteringUpdateEvent>(x.EventBody.ToString()),
    cancellationToken: cts.Token);

processorEvents.Subscribe(onNext: e => {
    Func<MeteringUpdateEvent, string> conv = (e) => MeteringUpdateEventModule.partitionKey(e);
    var str = EventHubProcessorEvent.toStr<MeteringUpdateEvent>(converter: conv.ToFSharpFunc(), e);
    Console.WriteLine(str);
});


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
