using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Metering;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Metering.Messaging;
using Microsoft.FSharp.Core;
using Metering.Types.EventHub;

Console.Title = Assembly.GetExecutingAssembly().GetName().Name;

DemoCredential config = DemoCredentials.Get(
    consumerGroupName: EventHubConsumerClient.DefaultConsumerGroupName);

#region Produce

Console.WriteLine(config.EventHubInformation.EventHubNamespaceName);

EventHubProducerClient producer = new(
    fullyQualifiedNamespace: $"{config.EventHubInformation.EventHubNamespaceName}.servicebus.windows.net",
    eventHubName: config.EventHubInformation.EventHubInstanceName,
    credential: config.TokenCredential,
    clientOptions: new()
    {
        ConnectionOptions = new()
        {
            TransportType = EventHubsTransportType.AmqpTcp,
        },
    });
            
using CancellationTokenSource producerCts = new();
// producerCts.CancelAfter(TimeSpan.FromSeconds(100));
var producerTask = Task.Run(async () =>
{
    var ids = (await producer.GetPartitionIdsAsync()).ToArray();
    string firstPartitionId = ids.First();

    try
    {
        await Console.Out.WriteLineAsync($"Partition {firstPartitionId}");
        while (!producerCts.IsCancellationRequested)
        {
            await Console.Out.WriteLineAsync("emit event");
            await producer.SubmitManagedAppIntegerAsync(meterName: "ML", quantity: 1);

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
    finally
    {
        await producer.CloseAsync();
    }
}, producerCts.Token);

#endregion

// https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Azure.Messaging.EventHubs/samples/Sample05_ReadingEvents.md

EventHubConsumerClient eventHubConsumerClient = new(
    consumerGroup: config.EventHubInformation.ConsumerGroup,
    fullyQualifiedNamespace: $"{config.EventHubInformation.EventHubNamespaceName}.servicebus.windows.net",
    eventHubName: config.EventHubInformation.EventHubInstanceName,
    credential: config.TokenCredential
    //, clientOptions: new EventHubConsumerClientOptions
    //{
    //    ConnectionOptions = new()
    //    {
    //        TransportType = EventHubsTransportType.AmqpTcp,
    //        //Proxy = new WebProxy(Address: "https://127.0.0.1:8888/", BypassOnLocal: false),
    //        //CertificateValidationCallback = (_sender, _certificate, _chain, _sslPolicyError) => true,
    //    },
    //}
);

var observable = Aggregator.CreateObservable(
        config: DemoCredentials.Get(EventHubConsumerClient.DefaultConsumerGroupName),
        someMessagePosition: FSharpOption<MessagePosition>.None,
        cancellationToken: producerCts.Token);

observable.Subscribe(onNext: x => {
    var (e, d) = x;
    Console.WriteLine($"{e} {x}");
});


//var ids = (await eventHubConsumerClient.GetPartitionIdsAsync()).ToArray();
//string firstPartitionId = ids.Skip(1).First();

//static async Task handle(PartitionEvent partitionEvent)
//{
//    var lastEnqueuedEvent = partitionEvent.Partition.ReadLastEnqueuedEventProperties();
//    var timedelta = lastEnqueuedEvent.LastReceivedTime.Value.Subtract(partitionEvent.Data.EnqueuedTime);
//    var sequenceDelta = lastEnqueuedEvent.SequenceNumber - partitionEvent.Data.SequenceNumber;

//    //string readFromPartition = partitionEvent.Partition.PartitionId;
//    //byte[] eventBodyBytes = partitionEvent.Data.EventBody.ToArray();
//    await Console.Out.WriteLineAsync($"partition {partitionEvent.Partition.PartitionId} sequence# {partitionEvent.Data.SequenceNumber} catchup {timedelta} ({sequenceDelta} events)");
//}

//try
//{
//    var runtime = TimeSpan.FromSeconds(60);
//    using CancellationTokenSource cts = new();
//    cts.CancelAfter(runtime);
//    await Console.Out.WriteLineAsync($"Start reading across all partitions now for {runtime}");



//    await foreach (PartitionEvent partitionEvent in eventHubConsumerClient.ReadEventsAsync(
//        startReadingAtEarliestEvent: true,
//        readOptions: new ReadEventOptions
//        {
//            TrackLastEnqueuedEventProperties = true,
//            // MaximumWaitTime = TimeSpan.FromSeconds(2),
//        },
//        cancellationToken: cts.Token))
//    {
//        await handle(partitionEvent);
//    }
//}
//catch (TaskCanceledException)
//{
//    await Console.Out.WriteLineAsync("Task cancelled");
//}
//finally
//{
//    // await eventHubConsumerClient.CloseAsync();
//    await Console.Out.WriteLineAsync("Stopped listening on all partitions");
//}

//try
//{
//    var runtime = TimeSpan.FromSeconds(60);
//    using CancellationTokenSource cts = new();
//    cts.CancelAfter(runtime);

//    await Console.Out.WriteLineAsync($"Start reading now from partition {firstPartitionId} (consumerGroup {config.EventHubInformation.ConsumerGroup}) for {runtime}:");

//	await foreach (PartitionEvent partitionEvent in eventHubConsumerClient.ReadEventsFromPartitionAsync(
//        partitionId: firstPartitionId,
//        startingPosition: EventPosition.Earliest, // EventPosition.FromSequenceNumber(sequenceNumber: 0),
//		readOptions: new()
//        {
//            TrackLastEnqueuedEventProperties = true,
//        },
//        cancellationToken: cts.Token))
//    {
//        await handle(partitionEvent);
//	}
//}
//catch (TaskCanceledException)
//{
//    Debug.WriteLine("Task cancelled");
//}
//finally
//{
//    await eventHubConsumerClient.CloseAsync();
//}

await Console.Out.WriteLineAsync("Press <Return> to close...");
_ = await Console.In.ReadLineAsync();
producerCts.Cancel();
