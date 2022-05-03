namespace Metering.EventHub;

using Avro.File;
using Avro.Generic;
using Azure;
using Azure.Messaging.EventHubs;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Metering.BaseTypes;
using Metering.BaseTypes.EventHub;
using Metering.Integration;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MeteringDateTime = NodaTime.ZonedDateTime;
using SequenceNumber = System.Int64;

internal record BlobNameAndTime(string BlobName, MeteringDateTime Time);

public static class CaptureProcessorCS
{
    public static EventHubEvent<TEvent> CreateFromEventHubCapture<TEvent>(this EventData data, Func<EventData, TEvent> convert, PartitionID partitionId, string blobName)
        => new(
            messagePosition: EventHubIntegration.createMessagePositionFromEventData(partitionId, data),
            eventsToCatchup: null,
            eventData: convert(data),
            source: EventSource.NewCapture(blobName));

    public static string? GetBlobName(this EventData e) => e switch
    {
        RehydratedFromCaptureEventData data => data.BlobName,
        _ => null,
    };

    public static EventData CreateEventDataFromBytes(string blobName, byte[] eventBody, long sequenceNumber, long offset, string partitionKey) =>
        new RehydratedFromCaptureEventData(blobName, eventBody,
            properties: new Dictionary<string, object>(), systemProperties: new Dictionary<string, object>(),
            sequenceNumber: sequenceNumber, offset: offset, enqueuedTime: DateTimeOffset.UtcNow, partitionKey: partitionKey);

    private static DateTimeOffset ParseTime(this string s) =>
        new(DateTime.ParseExact(s: s,
            format: "M/d/yyyy h:mm:ss tt",  // "12/2/2021 2:58:24 PM"
            provider: CultureInfo.InvariantCulture,
            style: DateTimeStyles.AssumeUniversal));

    private static long ParseLong(this string s) => Int64.Parse(s);

    private static T GetRequiredAvroProperty<T>(this GenericRecord record, string fieldName) =>
        record.TryGetValue(fieldName: fieldName, out var result) switch
        {
            true => (T)result,
            false => throw new ArgumentException($"Missing field {fieldName} in {nameof(GenericRecord)} object.")
        };

    private static string CreateRegexPattern(string captureFileNameFormat, EventHubName eventHubName, PartitionID partitionId) =>
        $"{captureFileNameFormat}.avro"
            .Replace("{Namespace}", eventHubName.NamespaceName)
            .Replace("{EventHub}", eventHubName.InstanceName)
            .Replace("{PartitionId}", partitionId.value())
            .Replace("{Year}", @"(?<year>\d{4})")
            .Replace("{Month}", @"(?<month>\d{2})")
            .Replace("{Day}", @"(?<day>\d{2})")
            .Replace("{Hour}", @"(?<hour>\d{2})")
            .Replace("{Minute}", @"(?<minute>\d{2})")
            .Replace("{Second}", @"(?<second>\d{2})");

    public static string GetPrefixForRelevantBlobs(string captureFileNameFormat, EventHubName eventHubName, PartitionID partitionId)
    {
        var regexPattern = CreateRegexPattern(captureFileNameFormat, eventHubName, partitionId);
        const string beginningOfACaptureGroup = "(?<";
        return regexPattern[..regexPattern.IndexOf(beginningOfACaptureGroup)];
    }

    private static int V(this Match match, string name) => int.Parse(match.Groups[name].Value);

    public static MeteringDateTime? ExtractTime(string captureFileNameFormat, string blobName, EventHubName eventHubName, PartitionID partitionId)
    {
        Regex regex = new(
            pattern: CreateRegexPattern(captureFileNameFormat, eventHubName, partitionId),
            options: RegexOptions.ExplicitCapture);

        var match = regex.Match(input: blobName);

        return match.Success switch
        {
            true => MeteringDateTimeModule.create(
                year: match.V("year"),
                month: match.V("month"),
                day: match.V("day"),
                hour: match.V("hour"),
                minute: match.V("minute"),
                second: match.V("second")),
            false => null!
        };
    }

    public static bool ContainsFullyRelevantEvents(this MeteringDateTime timeStampBlob, MeteringDateTime startTime)
        => (timeStampBlob - startTime).BclCompatibleTicks >= 0;

    public static bool IsRelevantBlob(string captureFileNameFormat, EventHubName eventHubName, PartitionID partitionId, string blobName, MeteringDateTime startTime)
        => ExtractTime(captureFileNameFormat, blobName, eventHubName, partitionId) switch
        {
            MeteringDateTime timeStampBlob => timeStampBlob.ContainsFullyRelevantEvents(startTime: startTime),
            null => false
        };

    public static IEnumerable<EventData> ReadEventDataFromAvroStream(this Stream stream, string blobName, CancellationToken cancellationToken = default)
    {
        // https://docs.microsoft.com/en-us/azure/event-hubs/event-hubs-capture-overview       
        using var reader = DataFileReader<GenericRecord>.OpenReader(stream);
        while (reader.HasNext())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }

            var genericAvroRecord = reader.Next();

            var sequenceNumber = genericAvroRecord.GetRequiredAvroProperty<long>("SequenceNumber");
            var offset = genericAvroRecord.GetRequiredAvroProperty<string>("Offset").ParseLong();
            var enqueuedTimeUtc = genericAvroRecord.GetRequiredAvroProperty<string>("EnqueuedTimeUtc").ParseTime();
            var systemProperties = genericAvroRecord.GetRequiredAvroProperty<Dictionary<string, object>>("SystemProperties").ToImmutableDictionary();
            var properties = genericAvroRecord.GetRequiredAvroProperty<Dictionary<string, object>>("Properties").ToImmutableDictionary();
            var body = genericAvroRecord.GetRequiredAvroProperty<byte[]>("Body");
            var partitionKey = systemProperties.ContainsKey("x-opt-partition-key") switch {
                true => (string)systemProperties["x-opt-partition-key"], // x-opt-partition-key: 3e7a30bd-29c3-0ae1-2cff-8fb87480823d
                false => ""
            };

            yield return new RehydratedFromCaptureEventData(
                blobName: blobName, eventBody: body,
                properties: properties, systemProperties: systemProperties,
                enqueuedTime: enqueuedTimeUtc, sequenceNumber: sequenceNumber,
                offset: offset, partitionKey: partitionKey);
        }
    }

    public static async IAsyncEnumerable<EventData> ReadCaptureFromPosition(
        this MeteringConnections connections,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var option = connections?.EventHubConfig.CaptureStorage;
        if (option.IsSome())
        {
            var captureContainer = connections!.EventHubConfig.CaptureStorage.Value.Storage;
            await foreach (var blob in captureContainer.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                var client = captureContainer.GetBlobClient(blobName: blob.Name);
                var d = await client.DownloadAsync(cancellationToken: cancellationToken);
                var items = d.Value.Content.ReadEventDataFromAvroStream(blobName: blob.Name, cancellationToken: cancellationToken);
                foreach (var item in items)
                {
                    yield return item;
                }
            }
        }
    }

    public static async Task<IEnumerable<string>> GetCaptureBlobs(
        this MeteringConnections connections, PartitionID partitionId, CancellationToken cancellationToken = default)
    {
        if (connections.EventHubConfig.CaptureStorage.IsNone())
        {
            return Enumerable.Empty<string>();
        }

        var captureStorage = connections!.EventHubConfig.CaptureStorage.Value;
        var prefix = GetPrefixForRelevantBlobs(captureStorage.CaptureFileNameFormat, connections.EventHubConfig.EventHubName, partitionId);
        List<string> result = new();
        await foreach (var pageableItem in captureStorage.Storage.GetBlobsByHierarchyAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            result.Add(pageableItem.Blob.Name);
        }
        return result;
    }

    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> enumerable) => enumerable.Where(x => x != null).Select(x => x!);

    public static async Task<string[]> ListRelevantBlobs(
        this MeteringConnections connections, 
        PartitionID partitionId, CancellationToken cancellationToken = default)
    {
        var captureStorage = connections.EventHubConfig.CaptureStorage.Value;

        MeteringDateTime? getTime(string blobName) => ExtractTime(
               captureStorage!.CaptureFileNameFormat, blobName, connections.EventHubConfig.EventHubName, partitionId);

        return 
            (await connections.GetCaptureBlobs(partitionId, cancellationToken))
            .Select(n =>
            {
                var t = getTime(n);
                return t == null ? null : new BlobNameAndTime(n, (MeteringDateTime)t);
            })
            .WhereNotNull()
            .Select(n => new { name = n.BlobName, instant = n.Time.ToInstant() })
            .OrderBy(n => n.instant)
            .Select(n => n.name)
            .ToArray();
    }

    public static async IAsyncEnumerable<TEvent> ReadAllEvents<TEvent>(
        MeteringConnections connections, PartitionID partitionId, Func<EventData, TEvent> convert,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (connections.EventHubConfig.CaptureStorage.IsSome())
        {
            var captureStorage = connections!.EventHubConfig.CaptureStorage.Value;

            IEnumerable<string> blobs = await connections.ListRelevantBlobs(partitionId, cancellationToken);

            foreach (var blobName in blobs)
            {
                BlobClient? client = captureStorage.Storage.GetBlobClient(blobName: blobName);
                Response<BlobDownloadStreamingResult>? downloadInfo = await client.DownloadStreamingAsync(cancellationToken: cancellationToken);
                IEnumerable<EventData>? events = downloadInfo?.Value?.Content?.ReadEventDataFromAvroStream(blobName, cancellationToken);
                if (events != null)
                {
                    foreach (var ev in events)
                    {
                        yield return convert(ev);
                    }
                }
            }
        }
    }

    public static async IAsyncEnumerable<EventHubEvent<TEvent>> ReadEventsFromPosition<TEvent>(
        MeteringConnections connections,
        MessagePosition messagePosition,
        Func<EventData, TEvent> convert,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (connections.EventHubConfig.CaptureStorage.IsSome())
        {
            var captureStorage = connections!.EventHubConfig.CaptureStorage.Value;

            MeteringDateTime? getTime(string blobName) => ExtractTime(
                captureStorage!.CaptureFileNameFormat, blobName, connections.EventHubConfig.EventHubName, messagePosition.PartitionID);
            var blobNames = await connections.GetCaptureBlobs(messagePosition.PartitionID, cancellationToken);
            var (startTime, sequenceNumber) = (messagePosition.PartitionTimestamp, messagePosition.SequenceNumber);

            bool fullyRelevant(BlobNameAndTime x) => x.Time.ContainsFullyRelevantEvents(startTime);
            BlobNameAndTime[] blobs =
                (await connections.GetCaptureBlobs(messagePosition.PartitionID, cancellationToken))
                .Select(n =>
                {
                    var t = getTime(n);
                    return t == null ? null : new BlobNameAndTime(n, (MeteringDateTime)t);
                })
                .Where(n => n != null)
                .Select(n => n!)
                .OrderBy(n => n.Time)
                .ToArray();

            var indexOfTheFirstFullyRelevantCaptureFileOption = Array.FindIndex(blobs, fullyRelevant);

            var indexOfTheFirstPartlyRelevantCaptureFile =
                indexOfTheFirstFullyRelevantCaptureFileOption == -1
                    ? blobs.Length
                    : indexOfTheFirstFullyRelevantCaptureFileOption - 1;
            IEnumerable<string> relevantBlobs =
                    blobs
                    .Skip(indexOfTheFirstPartlyRelevantCaptureFile)
                    .Select(n => n.BlobName);

            bool isRelevantEvent(EventHubEvent<TEvent> e) => messagePosition.SequenceNumber < e.MessagePosition.SequenceNumber;

            foreach (var blobName in relevantBlobs)
            {
                EventHubEvent<TEvent> toEvent(EventData eventData) => CreateFromEventHubCapture(eventData, convert, messagePosition.PartitionID, blobName!);
                
                BlobClient? client = captureStorage.Storage.GetBlobClient(blobName: blobName);
                Response<BlobDownloadStreamingResult>? downloadInfo = await client.DownloadStreamingAsync(cancellationToken: cancellationToken);
                
                var events = downloadInfo?.Value?.Content?
                    .ReadEventDataFromAvroStream(blobName, cancellationToken)
                    .Select(toEvent)
                    .Where(isRelevantEvent);

                if (events != null)
                {
                    foreach (var ev in events)
                    {
                        yield return ev;
                    }
                }
            }
        }
    }
}