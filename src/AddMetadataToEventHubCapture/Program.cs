// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.EventHubs;
using Metering.BaseTypes;
using Metering.EventHub;
using Metering.Integration;

/*
 * This utility can go through an Event Hub capture container, and add metadata to the capture files about what's included in them.
 */

static string DateTimeOffsetToString(DateTimeOffset d) => MeteringDateTimeModule.toStr(MeteringDateTimeModule.fromDateTimeOffset(d));
static (string, string, string) getData(EventData item) => (item.SequenceNumber.ToString(), item.Offset.ToString(), DateTimeOffsetToString(item.EnqueuedTime));

var captureContainer = MeteringConnections.getFromEnvironment().EventHubConfig.CaptureStorage.Value.Storage;
await foreach (var blob in captureContainer.GetBlobsAsync())
{
    var client = captureContainer.GetBlobClient(blob.Name);
    var content = await client.DownloadStreamingAsync();

    try
    {
        var events = CaptureProcessor.ReadEventDataFromAvroStream(blob.Name, content.Value.Content).ToArray();

        var (s, o, e) = getData(events.First());
        blob.Metadata.Add("firstSequenceNumber", s);
        blob.Metadata.Add("firstOffset", o);
        blob.Metadata.Add("firstEnqueuedTime", e);

        (s, o, e) = getData(events.Last());
        blob.Metadata.Add("lastSequenceNumber", s);
        blob.Metadata.Add("lastOffset", o);
        blob.Metadata.Add("lastEnqueuedTime", e);

        await client.SetMetadataAsync(blob.Metadata);
        await Console.Out.WriteLineAsync($"Updated metadata for {blob.Name}");
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"Error {blob.Name}: {ex.Message}") ;
    }
}