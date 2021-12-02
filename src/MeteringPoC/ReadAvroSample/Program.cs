using Avro;
using Avro.File;
using Avro.Generic;
using Azure.Messaging.EventHubs;
using System.Collections.Immutable;
using System.Globalization;

// https://docs.microsoft.com/en-us/azure/event-hubs/event-hubs-capture-overview

using var stream = File.OpenRead(@"C:\Users\chgeuer\Desktop\54.avro");

foreach (EventData eventData in stream.ReadAvroStreamToEventHubData().ToArray())
{
    Console.WriteLine($"sequenceNumber={eventData.SequenceNumber} partitionKey={eventData.PartitionKey} body={eventData.EventBody.ToString()}");
}

internal class MyEventData : EventData
{
    internal MyEventData(
        byte[] eventBody, 
        IDictionary<string, object> properties,
        IReadOnlyDictionary<string, object> systemProperties,
        long sequenceNumber,
        long offset,
        DateTimeOffset enqueuedTime,
        string partitionKey) : base(
            eventBody: eventBody,
            properties: properties, 
            systemProperties: systemProperties,
            sequenceNumber: sequenceNumber, 
            offset: offset, 
            enqueuedTime: enqueuedTime, 
            partitionKey: partitionKey) { }
}

public static class CaptureProcessorHostExtensions
{
    internal static async Task ForeachAwaiting<T>(this IEnumerable<T> values, Func<T, Task> action)
    {
        foreach (var value in values)
        {
            await action(value);
        }
    }

    internal static void Foreach<T>(this IEnumerable<T> values, Action<T> action)
    {
        foreach (var value in values)
        {
            action(value);
        }
    }

    private static T GetValue<T>(this GenericRecord record, string fieldName)
    {
        if (!record.TryGetValue(fieldName, out object result))
        {
            throw new ArgumentException($"Missing field {fieldName} in {nameof(GenericRecord)} object.");
        }
        return (T)result;
    }

    private static DateTime ParseTime(this string s) => DateTime.ParseExact(s, format: "M/d/yyyy h:mm:ss tt",
                provider: CultureInfo.InvariantCulture, style: DateTimeStyles.AssumeUniversal);

    public static IEnumerable<EventData> ReadAvroStreamToEventHubData(this Stream stream)
    {
        using var reader = DataFileReader<GenericRecord>.OpenReader(stream);
        while (reader.HasNext())
        {
            GenericRecord genericAvroRecord = reader.Next();

            // 0 SequenceNumber  long
            // 1 Offset          string          "8589956720"
            // 2 EnqueuedTimeUtc string "12/2/2021 2:58:24 PM"
            // 3 SystemProperties long/double/string/bytes     
            //    x-opt-partition-key : string = "3e7a30bd-29c3-0ae1-2cff-8fb87480823d"
            // 	  x-opt-enqueued-time : long = 1638457104816	
            // 4 Properties
            // 5 Body

            long sequenceNumber = genericAvroRecord.GetValue<long>("SequenceNumber");
            long offset = long.Parse(genericAvroRecord.GetValue<string>("Offset"));
            DateTime enqueuedTimeUtc = genericAvroRecord.GetValue<string>("EnqueuedTimeUtc").ParseTime();
            Dictionary<string, object> systemProperties = genericAvroRecord.GetValue<Dictionary<string, object>>("SystemProperties");
            Dictionary<string, object> properties = genericAvroRecord.GetValue<Dictionary<string, object>>("Properties");
            byte[] body = genericAvroRecord.GetValue<byte[]>(nameof(EventData.Body));

            string partitionKey = systemProperties["x-opt-partition-key"] as string;

            yield return new MyEventData(
                eventBody: body,
                properties: properties,
                systemProperties: systemProperties.ToImmutableDictionary(),
                sequenceNumber: sequenceNumber,
                enqueuedTime: enqueuedTimeUtc,
                offset: offset,
                partitionKey: partitionKey);
        }
    }
}
