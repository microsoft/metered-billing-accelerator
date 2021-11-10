using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Metering;

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

await producer.SubmitManagedAppIntegerAsync("ml", 1, CancellationToken.None);

Console.WriteLine("Submitted a meter");
