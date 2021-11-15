using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Metering;


#pragma warning disable CS8601 // Possible null reference assignment.
Console.Title = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
#pragma warning restore CS8601 // Possible null reference assignment.

using CancellationTokenSource producerCts = new();
// producerCts.CancelAfter(TimeSpan.FromSeconds(100));

var producerTask = Task.Run(async () =>
{
    var config = DemoCredentials.Get(consumerGroupName: EventHubConsumerClient.DefaultConsumerGroupName);

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

    try
    {
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

_ = Console.ReadLine();
producerCts.Cancel();
