// See https://aka.ms/new-console-template for more information
using Metering.Integration;

using Metering.EventHub;
using Metering.BaseTypes.EventHub;
using Metering.BaseTypes;
using static Metering.BaseTypes.MeteringUpdateEvent;

Console.WriteLine("Hello, World!");

var connections = MeteringConnectionsModule.getFromEnvironment();

var x = CaptureProcessorCS.ReadAllEvents(
    connections: connections,
    convert: CaptureProcessor.toMeteringUpdateEvent,
    partitionId: PartitionID.create("2"));

await foreach (MeteringUpdateEvent e in x)
{
    var msg =
        e switch
        {
            SubscriptionPurchased sp => $"Sub purchased {sp.Item.Subscription.InternalResourceId.ToString()}",
            MeteringUpdateEvent.UnprocessableMessage um => $"Unprocessable message {um.Item.ToString()}",
            SubscriptionDeletion d => $"Deleted {d.Item.ToString()}",
            UsageReported u => $"Usage {u.Item.InternalResourceId} {u.Item.Quantity}",
            UsageSubmittedToAPI u => $"Reported: {u.Item.Result.ToString()}",
            _ => $"Something else"
        };

    Console.WriteLine(msg);

    //var usageSubmitted = e as UsageSubmittedToAPI;
    //if (usageSubmitted != null)
    //{
    //    Console.WriteLine(usageSubmitted.Item.Result);
    //}
}