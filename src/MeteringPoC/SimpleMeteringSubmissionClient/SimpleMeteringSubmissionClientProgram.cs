using System.Security.Cryptography;
using System.Text;
using Metering;
using Metering.Types;

#pragma warning disable CS8601 // Possible null reference assignment.
Console.Title = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
#pragma warning restore CS8601 // Possible null reference assignment.

static string guidFromStr(string str) => new Guid(SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(str)).Take(16).ToArray()).ToString();

static async Task<T> readJson<T>(string name) => Json.fromStr<T>(await File.ReadAllTextAsync(name));

using CancellationTokenSource cts = new();

var config = MeteringConnectionsModule.getFromEnvironment();
var eventHubProducerClient = config.createEventHubProducerClient();

try
{
    var (subName,saasId) = ("", "");
    while (true)
    {
        await Console.Out.WriteLineAsync("c sub or s sub to submit to a particular subscription>  ");
        var command = await Console.In.ReadLineAsync();


        if (command.ToLower().StartsWith("c"))
        {
            subName = command.Split(' ')[1];
            saasId = guidFromStr(subName);

            SubscriptionCreationInformation sci = new(
                internalMetersMapping: await readJson<InternalMetersMapping>("mapping.json"),
                subscription: new(
                    plan: await readJson<Plan>("plan.json"),
                    internalResourceId: InternalResourceIdModule.fromStr(saasId),
                    renewalInterval: RenewalInterval.Monthly,
                    subscriptionStart: MeteringDateTimeModule.now()));

            await Console.Out.WriteLineAsync($"Creating subscription {subName} ({saasId})");
            await eventHubProducerClient.CreateSubscription(sci, cts.Token);
        }
        else if (command.ToLower().StartsWith("s"))
        {
            subName = command.Split(' ')[1];
            saasId = guidFromStr(subName);

            await Console.Out.WriteLineAsync($"Emitting to {subName} ({saasId})");
            await eventHubProducerClient.SubmitSaasIntegerAsync(saasId: saasId, meterName: "cpu", quantity: 1, cts.Token);
        }
        else
        {
            await Console.Out.WriteLineAsync($"Emitting to {subName} ({saasId})");
            await eventHubProducerClient.SubmitSaasIntegerAsync(saasId: saasId, meterName: "cpu", quantity: 1, cts.Token);
        }
    }
}
catch (Exception e)
{
    await Console.Error.WriteLineAsync(e.Message);
}
finally
{
    await eventHubProducerClient.CloseAsync();
    cts.Cancel();
}
