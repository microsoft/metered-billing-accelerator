using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Metering;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

Console.Title = Assembly.GetExecutingAssembly().GetName().Name;

DemoCredential config = DemoCredentials.Get(
    consumerGroupName: EventHubConsumerClient.DefaultConsumerGroupName);

#region Produce

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

    int idx = 0;
    try
    {
        await Console.Out.WriteLineAsync($"Partition {firstPartitionId}");
        while (!producerCts.IsCancellationRequested)
        {
            var id = (idx++) % ids.Length;
            var eventBatch = await producer.CreateBatchAsync(
                options: new CreateBatchOptions
                {
                    // PartitionKey = Guid.NewGuid().ToString(),
                    PartitionId = ids[id],
                },
                cancellationToken: producerCts.Token
            );
            EventData eventData = new(new BinaryData("This is an event body"));
            eventData.Properties.Add("SendingApplication", Assembly.GetExecutingAssembly().Location);
            if (!eventBatch.TryAdd(eventData))
            {
                throw new Exception($"The event could not be added.");
            }
            await producer.SendAsync(
                eventBatch: eventBatch, 
                // options: new SendEventOptions() {  PartitionId = "...", PartitionKey = "..." },
                cancellationToken: producerCts.Token);
            await Console.Out.WriteAsync($"{id}");
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


var ids = (await eventHubConsumerClient.GetPartitionIdsAsync()).ToArray();
string firstPartitionId = ids.Skip(1).First();

static async Task handle(PartitionEvent partitionEvent)
{
    var lastEnqueuedEvent = partitionEvent.Partition.ReadLastEnqueuedEventProperties();
    var timedelta = lastEnqueuedEvent.LastReceivedTime.Value.Subtract(partitionEvent.Data.EnqueuedTime);
    var sequenceDelta = lastEnqueuedEvent.SequenceNumber - partitionEvent.Data.SequenceNumber;

    //string readFromPartition = partitionEvent.Partition.PartitionId;
    //byte[] eventBodyBytes = partitionEvent.Data.EventBody.ToArray();
    await Console.Out.WriteLineAsync($"partition {partitionEvent.Partition.PartitionId} sequence# {partitionEvent.Data.SequenceNumber} catchup {timedelta} ({sequenceDelta} events)");
}

try
{
    var runtime = TimeSpan.FromSeconds(60);
    using CancellationTokenSource cts = new();
    cts.CancelAfter(runtime);
    await Console.Out.WriteLineAsync($"Start reading across all partitions now for {runtime}");

    await foreach (PartitionEvent partitionEvent in eventHubConsumerClient.ReadEventsAsync(
        startReadingAtEarliestEvent: true,
        readOptions: new ReadEventOptions
        {
            TrackLastEnqueuedEventProperties = true,
            // MaximumWaitTime = TimeSpan.FromSeconds(2),
        },
        cancellationToken: cts.Token))
    {
        await handle(partitionEvent);
    }
}
catch (TaskCanceledException)
{
    await Console.Out.WriteLineAsync("Task cancelled");
}
finally
{
    // await eventHubConsumerClient.CloseAsync();
    await Console.Out.WriteLineAsync("Stopped listening on all partitions");
}

try
{
    var runtime = TimeSpan.FromSeconds(60);
    using CancellationTokenSource cts = new();
    cts.CancelAfter(runtime);

    await Console.Out.WriteLineAsync($"Start reading now from partition {firstPartitionId} (consumerGroup {config.EventHubInformation.ConsumerGroup}) for {runtime}:");

	await foreach (PartitionEvent partitionEvent in eventHubConsumerClient.ReadEventsFromPartitionAsync(
        partitionId: firstPartitionId,
        startingPosition: EventPosition.Earliest, // EventPosition.FromSequenceNumber(sequenceNumber: 0),
		readOptions: new()
        {
            TrackLastEnqueuedEventProperties = true,
        },
        cancellationToken: cts.Token))
    {
        await handle(partitionEvent);
	}
}
catch (TaskCanceledException)
{
    Debug.WriteLine("Task cancelled");
}
finally
{
    await eventHubConsumerClient.CloseAsync();
}

await Console.Out.WriteLineAsync("Press <Return> to close...");
_ = await Console.In.ReadLineAsync();
producerCts.Cancel();
