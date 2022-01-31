// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Security.Cryptography;
using System.Text;
using Azure.Messaging.EventHubs.Producer;
using Metering.Types;
using Metering.ClientSDK;
using MeterValueModule = Metering.ClientSDK.MeterValueModule;
using System.Reflection.Metadata.Ecma335;

Console.Title = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;

var eventHubProducerClient = MeteringConnectionsModule.createEventHubProducerClientForClientSDK();

var subs = new[] {
    new SubSum("fdc778a6-1281-40e4-cade-4a5fc11f5440", "2021-11-04T16:12:26"),
    new SubSum("8151a707-467c-4105-df0b-44c3fca5880d", "2021-12-14T18:20:00")
};

using CancellationTokenSource cts = new();
//await CreateSubscriptions(eventHubProducerClient, subs, cts.Token);
//await ConsumeIncludedAtOnce(eventHubProducerClient, subs, cts.Token);
// await BatchKnownIDs(eventHubProducerClient, subs, cts.Token);

await BatchKnownIDs(eventHubProducerClient, subs, cts.Token);
cts.Cancel();

#pragma warning disable CS8321 // Local function is declared but never used
async Task CreateSubscriptions(EventHubProducerClient eventHubProducerClient, SubSum[] subscriptions, CancellationToken ct)
#pragma warning restore CS8321 // Local function is declared but never used
{
    foreach (var subscription in subscriptions)
    {
        var sub = new SubscriptionCreationInformation(
            internalMetersMapping: await readJson<InternalMetersMapping>("mapping.json"),
            subscription: new(
                plan: await readJson<Plan>("plan.json"),
                internalResourceId: InternalResourceIdModule.fromStr(subscription.Id),
                renewalInterval: RenewalInterval.Monthly,
                subscriptionStart: MeteringDateTimeModule.fromStr(subscription.Established)));

        await Console.Out.WriteLineAsync(Json.toStr(1, sub));
        await eventHubProducerClient.SubmitSubscriptionCreationAsync(sub, ct);
    }
}

/// <summary>
/// The current demo plans contain 10000 annual, and 1000 monthly units. This essentially consumes up all included quantities
/// </summary>
#pragma warning disable CS8321 // Local function is declared but never used
async Task ConsumeIncludedAtOnce(EventHubProducerClient eventHubProducerClient, SubSum[] subs, CancellationToken ct)
#pragma warning restore CS8321 // Local function is declared but never used
{
    foreach (var sub in subs)
    {
        var meters = new[] { "nde" }//, "cpu", "dta", "msg", "obj" }
               .Select(x => MeterValueModule.create(x, 11_000))
               .ToArray();

        await eventHubProducerClient.SubmitSaaSMeterAsync(SaaSConsumptionModule.create(sub.Id, meters), ct);
    }
}

async Task BatchKnownIDs(EventHubProducerClient eventHubProducerClient, SubSum[] subs, CancellationToken ct)
{
    Random random = new (); 
    
    int i = 0;
    while (true)
    {
        foreach (var sub in subs)
        {
            var meters = new[] { "nde", "cpu", "dta", "msg", "obj" }
                   .Select(x => MeterValueModule.create(x, random.NextDouble())).ToArray();

            if (i++ % 10 == 0) { Console.Write("."); }

            await eventHubProducerClient.SubmitSaaSMeterAsync(SaaSConsumptionModule.create(sub.Id, meters), ct);
            await Task.Delay(TimeSpan.FromSeconds(random.NextDouble() * 10), ct);
        }        
    }
}

static string guidFromStr(string str) => new Guid(SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(str)).Take(16).ToArray()).ToString();

static async Task<T> readJson<T>(string name) => Json.fromStr<T>(await File.ReadAllTextAsync(name));

#pragma warning disable CS8321 // Local function is declared but never used
static async Task BatchRandomId(EventHubProducerClient eventHubProducerClient, string subName, CancellationToken ct)
#pragma warning restore CS8321 // Local function is declared but never used
{
    int i = 0;
    var saasId = guidFromStr(subName);
    while (true)
    {
        var meters = new[] { "nde", "cpu", "dta", "msg", "obj" }
                   .Select(x => MeterValueModule.create(x, 0.1))
                   .ToArray();

        if (i++ % 10 == 0) { Console.Write("."); }

        await eventHubProducerClient.SubmitSaaSMeterAsync(SaaSConsumptionModule.create(saasId, meters), ct);
        await Task.Delay(TimeSpan.FromSeconds(0.3), ct);
    }
}

#pragma warning disable CS8321 // Local function is declared but never used
static async Task Interactive(EventHubProducerClient eventHubProducerClient, CancellationToken ct)
#pragma warning restore CS8321 // Local function is declared but never used
{
    static ulong GetQuantity(string command) => command.Split(' ').Length > 2 ? ulong.Parse(command.Split(' ')[2]) : 1;

    try
    {
        var (subName, saasId) = ("", "");
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
                await eventHubProducerClient.SubmitSubscriptionCreationAsync(sci, ct);
            }
            else if (command.ToLower().StartsWith("s"))
            {
                subName = command.Split(' ')[1];
                saasId = guidFromStr(subName);
                var count = GetQuantity(command);

                var meters = new[] { "nde", "cpu", "dta", "msg", "obj" }
                    .Select(x => MeterValueModule.create(x, count))
                    .ToArray();

                await Console.Out.WriteLineAsync($"Emitting to name={subName} (partitionKey={saasId})");
                await eventHubProducerClient.SubmitSaaSMeterAsync(SaaSConsumptionModule.create(saasId, meters), ct);
            }
            else
            {
                await Console.Out.WriteLineAsync($"Emitting to {subName} ({saasId})");
                var meters = new[] { "nde", "cpu", "dta", "msg", "obj" }
                    .Select(x => MeterValueModule.create(x, 1.0))
                    .ToArray();

                await Console.Out.WriteLineAsync($"Emitting to name={subName} (partitionKey={saasId})");
                await eventHubProducerClient.SubmitSaaSMeterAsync(SaaSConsumptionModule.create(saasId, meters), ct);
            }
        }
    }
    catch (Exception e)
    {
        await Console.Error.WriteLineAsync(e.Message);
    }
    finally
    {
        await eventHubProducerClient.CloseAsync(ct);
    }
}
