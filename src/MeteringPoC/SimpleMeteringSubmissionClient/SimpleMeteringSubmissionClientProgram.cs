using Azure.Messaging.EventHubs.Consumer;
using Metering;
using Metering.Types;
using System.Security.Cryptography;
using System.Text;

#pragma warning disable CS8601 // Possible null reference assignment.
Console.Title = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
#pragma warning restore CS8601 // Possible null reference assignment.

static string guidFromStr(string str) => new Guid(SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(str)).Take(16).ToArray()).ToString();

async Task<T> readJson<T>(string name) => Json.fromStr<T>(await File.ReadAllTextAsync(name));

using CancellationTokenSource cts = new();

_ = Task.Run(async () =>
{
    var config = DemoCredentials.Get(consumerGroupName: EventHubConsumerClient.DefaultConsumerGroupName);
    var eventHubProducerClient = config.CreateEventHubProducerClient();

    try
    {
        var saasId = guidFromStr("sub4");

        bool createSub = false;
        if (createSub)
        {
            SubscriptionCreationInformation sci = new(
                internalMetersMapping: await readJson<InternalMetersMapping>("mapping.json"),
                subscription: new(
                    plan: await readJson<Plan>("plan.json"),
                    internalResourceId: InternalResourceIdModule.fromStr(saasId),
                    renewalInterval: RenewalInterval.Monthly,
                    subscriptionStart: MeteringDateTimeModule.now()));
            await eventHubProducerClient.CreateSubscription(sci, cts.Token);
        }

        while (!cts.IsCancellationRequested)
        {
            await Console.Out.WriteLineAsync("emit event");
            await eventHubProducerClient.SubmitSaasIntegerAsync(saasId: saasId, meterName: "cpu", quantity: 1, cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
    finally
    {
        await eventHubProducerClient.CloseAsync();
    }
}, cts.Token);

_ = Console.ReadLine();
cts.Cancel();
