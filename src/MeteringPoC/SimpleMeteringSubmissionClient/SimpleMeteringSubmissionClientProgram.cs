using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Metering;
using Metering.Types;
using System.Security.Cryptography;
using System.Linq;
using System.Text;

#pragma warning disable CS8601 // Possible null reference assignment.
Console.Title = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
#pragma warning restore CS8601 // Possible null reference assignment.

static string guidFromStr(string str) => new Guid(SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(str)).Take(16).ToArray()).ToString();

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
        var saasId = guidFromStr("sub4");

        var planJson = @"{ ""planId"": ""plan2"", ""billingDimensions"": [
           { ""dimension"": ""MachineLearningJob"", ""name"": ""An expensive machine learning job"", ""unitOfMeasure"": ""machine learning jobs"", ""includedQuantity"": { ""monthly"": ""10"" } },
           { ""dimension"": ""EMailCampaign"", ""name"": ""An e-mail sent for campaign usage"", ""unitOfMeasure"": ""e-mails"", ""includedQuantity"": { ""monthly"": ""250000"" } } ] }";

        bool createSub = false;
        if (createSub)
        {
            SubscriptionCreationInformation sci = new(
                subscription: new(
                    plan: Json.fromStr<Plan>(planJson),
                    internalResourceId: InternalResourceIdModule.fromStr(saasId),
                    renewalInterval: RenewalInterval.Monthly,
                    subscriptionStart: MeteringDateTimeModule.now()),
                internalMetersMapping: Json.fromStr<InternalMetersMapping>(@"{ ""email"": ""EMailCampaign"", ""ml"": ""MachineLearningJob"" }"));
            await producer.CreateSubscription(sci, producerCts.Token);
        }

        while (!producerCts.IsCancellationRequested)
        {
            await Console.Out.WriteLineAsync("emit event");
            await producer.SubmitSaasIntegerAsync(saasId: saasId, meterName: "ml", quantity: 1, producerCts.Token);
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
