// See https://aka.ms/new-console-template for more information
using Metering.BaseTypes;
using Metering.Integration;
using Metering.EventHub;

static string DateTimeOffsetToString(DateTimeOffset d) => MeteringDateTimeModule.toStr(MeteringDateTimeModule.fromDateTimeOffset(d));

var configuration = MeteringConnectionsModule.getFromEnvironment();
var captureContainer = configuration.EventHubConfig.CaptureStorage.Value.Storage;
await foreach (var blob in captureContainer.GetBlobsAsync())
{
    var client = captureContainer.GetBlobClient(blob.Name);
    var content = await client.DownloadStreamingAsync();

    try
    {
        var events = CaptureProcessor.ReadEventDataFromAvroStream(blob.Name, content.Value.Content).ToArray();

        var first = events.First();
        var last = events.Last();

        blob.Metadata.Add("firstSequenceNumber", first.SequenceNumber.ToString());
        blob.Metadata.Add("lastSequenceNumber", last.SequenceNumber.ToString());
        blob.Metadata.Add("firstOffset", first.Offset.ToString());
        blob.Metadata.Add("lastOffset", last.Offset.ToString());
        blob.Metadata.Add("firstEnqueuedTime", DateTimeOffsetToString(first.EnqueuedTime));
        blob.Metadata.Add("lastEnqueuedTime", DateTimeOffsetToString(last.EnqueuedTime));

        await client.SetMetadataAsync(blob.Metadata);
        await Console.Out.WriteLineAsync($"Wrote {blob.Name}");
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"Error {blob.Name}: {ex.Message}") ;
    }
}