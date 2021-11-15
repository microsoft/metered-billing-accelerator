using Azure.Messaging.EventHubs.Consumer;
using Metering;
using Metering.Messaging;
using Metering.Types;
using Metering.Types.EventHub;
using Microsoft.FSharp.Core;
using System;
using System.Reflection;
using System.Threading;

Console.Title = Assembly.GetExecutingAssembly().GetName().Name;

DemoCredential config = DemoCredentials.Get(
    consumerGroupName: EventHubConsumerClient.DefaultConsumerGroupName);

Console.WriteLine(config.EventHubInformation.EventHubNamespaceName);

using CancellationTokenSource producerCts = new();

// https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Azure.Messaging.EventHubs/samples/Sample05_ReadingEvents.md

EventHubConsumerClient eventHubConsumerClient = new(
    consumerGroup: config.EventHubInformation.ConsumerGroup,
    fullyQualifiedNamespace: $"{config.EventHubInformation.EventHubNamespaceName}.servicebus.windows.net",
    eventHubName: config.EventHubInformation.EventHubInstanceName,
    credential: config.TokenCredential
);

var processor = EventHubConnectionDetailsModule.createProcessor(config.GetEventHubConnectionDetails());
IObservable<EventHubProcessorEvent> processorEvents = processor.CreateObservable(
    determinePosition: async (arg, ct) =>
        {
            var someMeters = await MeterCollectionStore.loadLastState(
                snapshotContainerClient: config.GetSnapshotStorage(),
                partitionID: arg.PartitionId,
                cancellationToken: producerCts.Token);

            var lastUpdate = MeterCollectionModule.lastUpdate(someMeters);
            var eventPosition = MessagePositionModule.startingPosition(lastUpdate);

            return eventPosition;
        },
    cancellationToken: producerCts.Token);

IObservable<(MeteringEvent, EventsToCatchup)> observable2 = EventHubObservableClient.CreateAggregatorObservable(
        config: DemoCredentials.Get(EventHubConsumerClient.DefaultConsumerGroupName),
        someMessagePosition: FSharpOption<MessagePosition>.None,
        cancellationToken: producerCts.Token);

processorEvents.Subscribe(onNext: e => {
    Console.WriteLine($"{e}");
});

observable2.Subscribe(onNext: x => {
    var (e, d) = x;
    Console.WriteLine($"{e} {x}");
});

await Console.Out.WriteLineAsync("Press <Return> to close...");
_ = await Console.In.ReadLineAsync();
producerCts.Cancel();
