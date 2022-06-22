// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Security.Cryptography;
using System.Text;
using Azure.Messaging.EventHubs.Producer;
using Metering.BaseTypes;
using Metering.ClientSDK;
using Metering.Integration;

Console.Title = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;

var eventHubProducerClient = MeteringConnections.createEventHubProducerClientForClientSDK();

Console.WriteLine($"namespace: {eventHubProducerClient.FullyQualifiedNamespace}");

var subs = new[] {
    new SubSum("0852f7eb-a664-4683-d707-e2350edcfee9", "2022-06-02T23:37:34")
};

using CancellationTokenSource cts = new();
await DeleteSubscriptions(eventHubProducerClient, subs, cts.Token);
//await CreateSubscriptions(eventHubProducerClient, subs, cts.Token);
//await ConsumeIncludedAtOnce(eventHubProducerClient, subs, cts.Token);
//await BatchKnownIDs(eventHubProducerClient, subs, cts.Token);
cts.Cancel();

#pragma warning disable CS8321 // Local function is declared but never used

static async Task DeleteSubscriptions(EventHubProducerClient eventHubProducerClient, SubSum[] subscriptions, CancellationToken ct)
{
    foreach (var subscription in subscriptions)
    {
        await eventHubProducerClient.SubmitSubscriptionDeletionAsync(InternalResourceId.fromStr(subscription.Id), ct);
    }
}
static async Task CreateSubscriptions(EventHubProducerClient eventHubProducerClient, SubSum[] subscriptions, CancellationToken ct)
#pragma warning restore CS8321 // Local function is declared but never used
{
    
    foreach (var subscription in subscriptions)
    {

        var sub = new SubscriptionCreationInformation(
            internalMetersMapping: await readJson<InternalMetersMapping>("mapping.json"),
            subscription: new(
                plan: await readJson<Plan>("plan.json"),
                internalResourceId: InternalResourceId.fromStr(subscription.Id),
                renewalInterval: RenewalInterval.Monthly,
                subscriptionStart: MeteringDateTimeModule.fromStr(subscription.Established)));

        await Console.Out.WriteLineAsync(Json.toStr(1, sub));
        await eventHubProducerClient.SubmitSubscriptionCreationAsync(sub, ct);

        await eventHubProducerClient.SubmitSubscriptionDeletionAsync(InternalResourceId.fromStr(subscription.Id), ct);

            
     }
}

/// <summary>
/// The current demo plans contain 1000 monthly units. This essentially consumes up all included quantities (except one)
/// </summary>
#pragma warning disable CS8321 // Local function is declared but never used
static async Task ConsumeIncludedAtOnce(EventHubProducerClient eventHubProducerClient, SubSum[] subs, CancellationToken ct)
#pragma warning restore CS8321 // Local function is declared but never used
{
    foreach (var sub in subs)
    {
        foreach (var meter in new[] { "cpu1", "mem1" })
        {
            await eventHubProducerClient.SubmitSaaSMeterAsync(
                saasSubscriptionId: sub.Id,
                applicationInternalMeterName: meter,
                quantity: 999,
                cancellationToken: ct);
        }
    }
}

//async Task BatchKnownIDs(EventHubProducerClient eventHubProducerClient, SubSum[] subs, CancellationToken ct)
//{
//    Random random = new (); 
    
//    int i = 0;
//    while (true)
//    {
//        foreach (var sub in subs)
//        {
//            var meters = new[] { "nde", "cpu", "dta", "msg", "obj" }
//                   .Select(x => MeterValues.create(x, random.NextDouble())).ToArray();

//            if (i++ % 10 == 0) { Console.Write("."); }

//            await eventHubProducerClient.SubmitSaaSMetersAsync(sub.Id, meters, ct);
//            await Task.Delay(TimeSpan.FromSeconds(random.NextDouble() * 10), ct);
//        }        
//    }
//}

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
        var meters = new[] { "cpu1", "mem1" }
                   .Select(x => MeterValues.create(x, 0.1))
                   .ToArray();

        if (i++ % 10 == 0) { Console.Write("."); }

        await eventHubProducerClient.SubmitSaaSMetersAsync(saasId, meters, ct);
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
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
                        internalResourceId: InternalResourceId.fromStr(saasId),
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
                    .Select(x => MeterValues.create(x, count))
                    .ToArray();

                await Console.Out.WriteLineAsync($"Emitting to name={subName} (partitionKey={saasId})");
                await eventHubProducerClient.SubmitSaaSMetersAsync(saasId, meters, ct);
            }
            else
            {
                await Console.Out.WriteLineAsync($"Emitting to {subName} ({saasId})");
                var meters = new[] { "nde", "cpu", "dta", "msg", "obj" }
                    .Select(x => MeterValues.create(x, 1.0))
                    .ToArray();

                await Console.Out.WriteLineAsync($"Emitting to name={subName} (partitionKey={saasId})");
                await eventHubProducerClient.SubmitSaaSMetersAsync(saasId, meters, ct);
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
