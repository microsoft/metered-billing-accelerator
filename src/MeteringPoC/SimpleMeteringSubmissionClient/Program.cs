using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Metering;

EventHubProducerClient producer = CreateEventHubProducer();

await producer.SubmitManagedAppIntegerAsync(
    meterName: "ml",
    quantity: 1);

Console.WriteLine("Submitted a meter");





static EventHubProducerClient CreateEventHubProducer()
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

    return producer;
}