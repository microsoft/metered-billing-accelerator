using Metering.Types;
using Metering.Types.EventHub;

var c = MeteringConnectionsModule.getFromEnvironment();
foreach (var eventData in c.ReadCaptureFromPosition())
{
    Console.WriteLine($"sequenceNumber={eventData.SequenceNumber} partitionKey={eventData.PartitionKey} body={eventData.EventBody}\n\n\n");
}
