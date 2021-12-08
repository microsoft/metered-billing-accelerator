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
open FSharp.Control
open Avro.File
open Avro.Generic
open Metering.Types

[<Extension>]
module CaptureProcessor = 
    open Metering.Types.EventHub.Capture

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

        regexPattern
        |> (fun s -> s.Substring(startIndex = 0, length = s.IndexOf(beginningOfACaptureGroup)))
        |> (fun s -> s.Substring(startIndex = 0, length = s.LastIndexOf("/") - 1))
    
    let extractTime (captureFileNameFormat: string) ((namespaceName: string), (eventHubName: string), (partitionId: string)) (blobName: string) : MeteringDateTime option = 
        let regex = 
            new Regex(
                pattern = createRegexPattern captureFileNameFormat (namespaceName, eventHubName, partitionId), 
                options = RegexOptions.ExplicitCapture)

        let matcH = regex.Match(input = blobName)        
        match matcH.Success with 
        | false -> None
        | true ->
            let g (name: string) = matcH.Groups[name].Value |> Int32.Parse            
            MeteringDateTime.create ("year" |> g) ("month" |> g) ("day" |> g) ("hour" |> g) ("minute" |> g) ("second" |> g)
            |> Some

    let containsFullyRelevantEvents (startTime: MeteringDateTime) (timeStampBlob: MeteringDateTime)  : bool =
        (timeStampBlob - startTime).BclCompatibleTicks >= 0
        
    let isRelevantBlob (captureFileNameFormat: string) ((namespaceName: string), (eventHubName: string), (partitionId: string)) (blobName: string) (startTime: MeteringDateTime): bool = 
        let blobtime = extractTime captureFileNameFormat (namespaceName, eventHubName, partitionId) blobName
        match blobtime with
        | None -> false
        | Some timeStampBlob -> timeStampBlob |> containsFullyRelevantEvents startTime
               
    [<Extension>]
    let internal ReadEventDataFromAvroStream (blobName: string) (stream: Stream) : IEnumerable<RehydratedFromCaptureEventData> =
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

                yield new RehydratedFromCaptureEventData(
                    blobName = blobName, eventBody = body, 
                    properties = properties, systemProperties = systemProperties,
                    enqueuedTime = enqueuedTimeUtc, sequenceNumber = sequenceNumber,
                    offset = offset, partitionKey = partitionKey)
        }

    let readCaptureFromPosition
            (cancellationToken: CancellationToken) 
            (connections: MeteringConnections)
            : IEnumerable<EventData> =
        match connections.EventHubConfig.CaptureStorage with
        | None -> Seq.empty
        | Some { CaptureFileNameFormat = captureFileNameFormat; Storage = captureContainer } -> 
            seq {
                // let blobs = captureContainer.GetBlobsByHierarchyAsync(prefix = "", cancellationToken = cancellationToken)
                let blobs = captureContainer.GetBlobs(cancellationToken = cancellationToken)
                for page in blobs.AsPages() do
                    for item in page.Values do
                        let blobName = item.Name
                        let client = captureContainer.GetBlobClient(blobName = blobName)
                        let d = client.Download(cancellationToken)
                        use stream = d.Value.Content
                        let items = stream |> ReadEventDataFromAvroStream blobName
                        for i in items do
                            yield i
            }

    [<Extension>]
    let ReadCaptureFromPosition connections ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) = 
        connections |> readCaptureFromPosition cancellationToken
        
    let getCaptureBlobs (cancellationToken: CancellationToken) (partitionId: string) (connections: MeteringConnections) : string seq =
        match connections.EventHubConfig.CaptureStorage with
        | None -> Seq.empty
        | Some { CaptureFileNameFormat = captureFileNameFormat; Storage = captureContainer } -> 
            seq {
                // TODO This should be done using a seqAsync or something else that supports proper async pagination in F# with the Azure SDK
                // https://docs.microsoft.com/en-us/dotnet/azure/sdk/pagination
                // https://github.com/Azure/azure-sdk-for-net/issues/18306

                let ehInfo = (connections.EventHubConfig.NamespaceName, connections.EventHubConfig.InstanceName, partitionId)
                let prefix = getPrefixForRelevantBlobs captureFileNameFormat ehInfo

                let pageableItems = captureContainer.GetBlobsByHierarchy(prefix = prefix, cancellationToken = cancellationToken)
                for page in pageableItems.AsPages() do
                    for blobHierarchyItem in page.Values do
                        yield blobHierarchyItem.Blob.Name
            }

    let readEventsFromPosition<'TEvent> (convert: EventData -> 'TEvent) (mp: MessagePosition) (cancellationToken: CancellationToken) (connections: MeteringConnections) : IEnumerable<EventHubEvent<'TEvent>> =
        match connections.EventHubConfig.CaptureStorage with
        | None -> Seq.empty
        | Some { CaptureFileNameFormat = captureFileNameFormat; Storage = captureContainer } ->
            seq {
                let partitionId = mp.PartitionID |> PartitionID.value
                let ehInfo = (connections.EventHubConfig.NamespaceName, connections.EventHubConfig.InstanceName, partitionId)
                let getTime = extractTime captureFileNameFormat ehInfo
                let blobNames = getCaptureBlobs cancellationToken partitionId connections
                let (partitionId, startTime, sequenceNumber) = (mp.PartitionID |> PartitionID.value, mp.PartitionTimestamp, mp.SequenceNumber)
                let fullyRelevant (blobName: string, timeStampBlob: MeteringDateTime) = 
                    timeStampBlob |> containsFullyRelevantEvents startTime

                let blobs = 
                    blobNames
                    |> Seq.map (fun n -> (n, n |> getTime))
                    |> Seq.filter (fun (_, t) -> t.IsSome)
                    |> Seq.map (fun (n, t) -> (n, t.Value))
                    |> Seq.sortBy (fun (blob, t) -> t.ToInstant())
                    |> Seq.toArray

                // Using EventHub capture, you have cannot easily correlate the filename to a sequence number or offset. 
                // You need to use the date/time to search. If you're looking for a given timestamp, you must select the 
                // very last capture file which is *before* that timestamp:
                //
                // For example, if you have the capture files 
                // - 2021-12-06--15-12-12
                // - 2021-12-06--15-16-12 <-- You need this one to get 2021-12-06--15-17-10
                // - 2021-12-06--15-17-12, 
                // and you're looking for an event with timestamp 2021-12-06--15-17-10, 
                // you must select the last one with a timestamp prior the lookup one.

                let indexOfTheFirstFullyRelevantCaptureFile = blobs |> Array.findIndex fullyRelevant
                let indexOfTheFirstPartlyRelevantCaptureFile = indexOfTheFirstFullyRelevantCaptureFile - 1

                let relevantBlobs =
                    blobs
                    |> Array.skip indexOfTheFirstPartlyRelevantCaptureFile
                    |> Array.map (fun (n,t) -> n)
                
                for blobName in relevantBlobs do                
                    let toEvent = EventHubEvent.createFromEventHubCapture convert partitionId blobName
                    let client = captureContainer.GetBlobClient(blobName = blobName)
                    let downloadInfo = client.Download(cancellationToken)
                    let items = 
                        downloadInfo.Value.Content
                        |> ReadEventDataFromAvroStream blobName
                    for i in items do
                        match i |> toEvent with
                        | None -> ()
                        | Some e -> 
                            let emitEvent = sequenceNumber < e.MessagePosition.SequenceNumber // only emit events after the sequence number we already have processed
                            if emitEvent
                            then 
                                yield e
            }
    // https://docs.microsoft.com/en-us/dotnet/azure/sdk/pagination
    // https://github.com/Azure/azure-sdk-for-net/issues/18306
    
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
