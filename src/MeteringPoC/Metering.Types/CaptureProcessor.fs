namespace Metering.Types.EventHub

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Globalization
open System.IO
open System.Text.RegularExpressions
open System.Threading
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Avro.File
open Avro.Generic
open Metering.Types

[<Extension>]
module CaptureProcessor = 
    type private RehydratedFromCaptureEventData(eventBody: byte[], properties: IDictionary<string, obj>, systemProperties: IReadOnlyDictionary<string, obj>, sequenceNumber: int64, offset: int64, enqueuedTime: DateTimeOffset, partitionKey: string) = 
        inherit EventData(new BinaryData(eventBody), properties, systemProperties, sequenceNumber, offset, enqueuedTime, partitionKey)
    
    let private ParseTime s =         
        // "12/2/2021 2:58:24 PM"
        DateTime.ParseExact(
            s = s,
            format = "M/d/yyyy h:mm:ss tt",
            provider = CultureInfo.InvariantCulture,
            style = DateTimeStyles.AssumeUniversal) 
        |> DateTimeOffset

    let private GetRequiredAvroProperty<'T> (fieldName : string) (record: GenericRecord) =
        match record.TryGetValue fieldName with
        | true, value -> value :?> 'T
        | false, _ -> raise <| new ArgumentException($"Missing field {fieldName} in {nameof(GenericRecord)} object.");

    let private createRegexPattern (captureFileNameFormat: string) ((namespaceName: string), (eventHubName: string), (partitionId: string)) : string =
        $"{captureFileNameFormat}.avro"
            .Replace("{Namespace}", namespaceName)
            .Replace("{EventHub}", eventHubName)
            .Replace("{PartitionId}", partitionId)
            .Replace("{Year}", "(?<year>\d{4})")
            .Replace("{Month}", "(?<month>\d{2})")
            .Replace("{Day}", "(?<day>\d{2})")
            .Replace("{Hour}", "(?<hour>\d{2})")
            .Replace("{Minute}", "(?<minute>\d{2})")
            .Replace("{Second}", "(?<second>\d{2})")

    let getPrefixForRelevantBlobs (captureFileNameFormat: string) ((namespaceName: string), (eventHubName: string), (partitionId: string)) : string =
        let regexPattern = createRegexPattern captureFileNameFormat (namespaceName, eventHubName, partitionId)
        let beginningOfACaptureGroup = "(?<"
        regexPattern.Substring(startIndex = 0, length = regexPattern.IndexOf(beginningOfACaptureGroup))
    
    let isRelevantBlob (captureFileNameFormat: string) ((namespaceName: string), (eventHubName: string), (partitionId: string)) (blobName: string) (time: MeteringDateTime): bool = 
        // Take an input format from , archiveDescription.destination.properties.archiveNameFormat
        // https://docs.microsoft.com/en-us/azure/event-hubs/event-hubs-resource-manager-namespace-event-hub-enable-capture#capturenameformat
        // "{Namespace}/{EventHub}/p{PartitionId}--{Year}-{Month}-{Day}--{Hour}-{Minute}-{Second}"
        // and convert it into a regex, so we can check if a given blob fits the bill
        // meteringhack-standard/hub2/p0--2021-12-06--15-17-12.avro
    
        let regex = 
            new Regex(
                pattern = createRegexPattern captureFileNameFormat (namespaceName, eventHubName, partitionId), 
                options = RegexOptions.ExplicitCapture)
    
        let matcH = regex.Match(input = blobName)        
        match matcH.Success with 
        | false -> false
        | true ->
            let g (name: string) = matcH.Groups[name].Value |> Int32.Parse
            let t = MeteringDateTime.create ("year" |> g) ("month" |> g) ("day" |> g) ("hour" |> g) ("minute" |> g) ("second" |> g)
            (t - time).BclCompatibleTicks >= 0;
    

    [<Extension>]
    let ReadEventDataFromAvroStream (stream: Stream) : IEnumerable<EventData> =
        // https://docs.microsoft.com/en-us/azure/event-hubs/event-hubs-capture-overview        
        seq {
            use reader = DataFileReader<GenericRecord>.OpenReader(stream)
            while reader.HasNext() do
                let genericAvroRecord : GenericRecord = reader.Next()

                let sequenceNumber = genericAvroRecord |> GetRequiredAvroProperty<int64> "SequenceNumber"
                let offset = genericAvroRecord |> GetRequiredAvroProperty<string> "Offset" |> Int64.Parse
                let enqueuedTimeUtc = genericAvroRecord |> GetRequiredAvroProperty<string> "EnqueuedTimeUtc" |> ParseTime        
                let systemProperties = genericAvroRecord |> GetRequiredAvroProperty<Dictionary<string, obj>> "SystemProperties" |> ImmutableDictionary.ToImmutableDictionary
                let properties = genericAvroRecord |> GetRequiredAvroProperty<IDictionary<string, obj>> "Properties"
                let body = genericAvroRecord |> GetRequiredAvroProperty<byte[]> "Body"
                let partitionKey = systemProperties["x-opt-partition-key"] :?> string // x-opt-partition-key : string = "3e7a30bd-29c3-0ae1-2cff-8fb87480823d"

                yield new RehydratedFromCaptureEventData(eventBody = body, 
                    properties = properties, systemProperties = systemProperties,
                    enqueuedTime = enqueuedTimeUtc, sequenceNumber = sequenceNumber,
                    offset = offset, partitionKey = partitionKey)
        }

    let readCaptureFromPosition
            (cancellationToken: CancellationToken) 
            (connections: MeteringConnections)
            : IEnumerable<EventData> =
        // failwith "completely untested"
        match connections.EventHubConfig.CaptureStorage with
        | None -> Seq.empty
        | Some captureContainer -> 
            seq {
                // let blobs = captureContainer.GetBlobsByHierarchyAsync(prefix = "", cancellationToken = cancellationToken)
                let blobs = captureContainer.GetBlobs(cancellationToken = cancellationToken)
                for page in blobs.AsPages() do
                    for item in page.Values do
                        let client = captureContainer.GetBlobClient(blobName = item.Name)
                        let d = client.Download(cancellationToken)
                        use stream = d.Value.Content
                        let items = stream |> ReadEventDataFromAvroStream
                        for i in items do
                            yield i
            }

    [<Extension>]
    let ReadCaptureFromPosition connections ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) = 
        connections |> readCaptureFromPosition cancellationToken
        
        

    
    //let GetBlobNames (snapshotContainerClient: BlobContainerClient) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
    //    task {
    //        let blobs = snapshotContainerClient.GetBlobsAsync(cancellationToken = cancellationToken)
    //        let n = blobs.GetAsyncEnumerator(cancellationToken = cancellationToken)
    //        let resultList = new List<string>()
    //        let! h = n.MoveNextAsync()
    //        let mutable hasNext = h
    //        while hasNext do
    //            let item = n.Current
    //            resultList.Add(item.Properties.CreatedOn.ToString())
    //            let! h = n.MoveNextAsync()
    //            hasNext <- h
    //        return resultList
    //    }    
