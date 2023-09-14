﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.EventHub

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Globalization
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Azure.Messaging.EventHubs
open FSharp.Control
open Avro.File
open Avro.Generic
open Metering.BaseTypes
open Metering.BaseTypes.EventHub
open Metering.Integration

[<Extension>]
module CaptureProcessor =
    open EventHubIntegration

    let createFromEventHubCapture (convert: EventData -> 'TEvent)  (partitionId: PartitionID) (blobName: string) (data: EventData) : EventHubEvent<'TEvent> option =
        { MessagePosition = createMessagePositionFromEventData partitionId data
          EventsToCatchup = None
          EventData = data |> convert
          Source = Capture(BlobName = blobName)}
        |> Some

    let getBlobName (e: EventData) : string option =
        match e with
        | :? RehydratedFromCaptureEventData -> (downcast e : RehydratedFromCaptureEventData) |> (fun x -> x.BlobName) |> Some
        | _ -> None

    let createEventDataFromBytes (blobName: string) (eventBody: byte[]) (sequenceNumber: int64) (offset: int64)  (partitionKey: string) : EventData =
        new RehydratedFromCaptureEventData(blobName, eventBody,
            new Dictionary<string,obj>(), new Dictionary<string,obj>(), sequenceNumber, offset, DateTimeOffset.UtcNow, partitionKey)

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

    let private createRegexPattern (captureFileNameFormat: string) ((eventHubName: EventHubName), (partitionId: PartitionID)) : string =
        $"{captureFileNameFormat}.avro"
            .Replace("{Namespace}", eventHubName.NamespaceName)
            .Replace("{EventHub}", eventHubName.InstanceName)
            .Replace("{PartitionId}", partitionId.value)
            .Replace("{Year}", "(?<year>\d{4})")
            .Replace("{Month}", "(?<month>\d{2})")
            .Replace("{Day}", "(?<day>\d{2})")
            .Replace("{Hour}", "(?<hour>\d{2})")
            .Replace("{Minute}", "(?<minute>\d{2})")
            .Replace("{Second}", "(?<second>\d{2})")

    let getPrefixForRelevantBlobs (captureFileNameFormat: string) ((eventHubName: EventHubName), (partitionId: PartitionID)) : string =
        let regexPattern = createRegexPattern captureFileNameFormat (eventHubName, partitionId)
        let beginningOfACaptureGroup = "(?<"

        regexPattern
        |> (fun s -> s.Substring(startIndex = 0, length = s.IndexOf(beginningOfACaptureGroup)))
        // |> (fun s -> s.Substring(startIndex = 0, length = s.LastIndexOf("/") - 1))

    let extractTime (captureFileNameFormat: string) ((eventHubName: EventHubName), (partitionId: PartitionID)) (blobName: string) : MeteringDateTime option =
        let regex =
            new Regex(
                pattern = createRegexPattern captureFileNameFormat (eventHubName, partitionId),
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

    let isRelevantBlob (captureFileNameFormat: string) ((eventHubName: EventHubName), (partitionId: PartitionID)) (blobName: string) (startTime: MeteringDateTime): bool =
        let blobtime = extractTime captureFileNameFormat (eventHubName, partitionId) blobName
        match blobtime with
        | None -> false
        | Some timeStampBlob -> timeStampBlob |> containsFullyRelevantEvents startTime

    [<Extension>]
    let ReadEventDataFromAvroStream (blobName: string) (stream: Stream) : IEnumerable<EventData> =
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
                let partitionKey =
                    if systemProperties.ContainsKey("x-opt-partition-key")
                    then systemProperties["x-opt-partition-key"] :?> string // x-opt-partition-key : string = "3e7a30bd-29c3-0ae1-2cff-8fb87480823d"
                    else ""

                yield new RehydratedFromCaptureEventData(
                    blobName = blobName, eventBody = body,
                    properties = properties, systemProperties = systemProperties,
                    enqueuedTime = enqueuedTimeUtc, sequenceNumber = sequenceNumber,
                    offset = offset, partitionKey = partitionKey)
        }

    let readCaptureFromPosition (cancellationToken: CancellationToken) (connections: MeteringConnections) : IEnumerable<EventData> =
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
    let ReadCaptureFromPosition (connections: MeteringConnections) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        connections |> readCaptureFromPosition cancellationToken

    /// Provide a list of all EventHub Capture blobs which belong to a given partition.
    let getCaptureBlobs (cancellationToken: CancellationToken) (partitionId: PartitionID) (connections: MeteringConnections) : string seq =
        match connections.EventHubConfig.CaptureStorage with
        | None -> Seq.empty
        | Some { CaptureFileNameFormat = captureFileNameFormat; Storage = captureContainer } ->
            let ehInfo = (connections.EventHubConfig.EventHubName, partitionId)
            let prefix = getPrefixForRelevantBlobs captureFileNameFormat ehInfo

            seq {
                // TODO This should be done using a seqAsync or something else that supports proper async pagination in F# with the Azure SDK
                // https://docs.microsoft.com/en-us/dotnet/azure/sdk/pagination
                // https://github.com/Azure/azure-sdk-for-net/issues/18306

                let pageableItems = captureContainer.GetBlobsByHierarchy(prefix = prefix, cancellationToken = cancellationToken)
                for page in pageableItems.AsPages() do
                    for blobHierarchyItem in page.Values do
                        yield blobHierarchyItem.Blob.Name
            }

    let readAllEvents<'TEvent> (convert: EventData -> 'TEvent) (partitionId: PartitionID) (cancellationToken: CancellationToken) (connections: MeteringConnections) : IEnumerable<EventHubEvent<'TEvent>> =
           match connections.EventHubConfig.CaptureStorage with
           | None -> Seq.empty
           | Some { CaptureFileNameFormat = captureFileNameFormat; Storage = captureContainer } ->
               seq {
                    let ehInfo = (connections.EventHubConfig.EventHubName, partitionId)
                    let getTime = extractTime captureFileNameFormat ehInfo
                    let blobNames = getCaptureBlobs cancellationToken partitionId connections

                    let blobs =
                        blobNames
                        |> Seq.map (fun n -> (n, n |> getTime))
                        |> Seq.choose (fun (n, time) -> time |> Option.map (fun t -> (n, t))) // instead of |> Seq.filter (fun (_, t) -> t.IsSome) |> Seq.map (fun (n, t) -> (n, t.Value.ToInstant()))
                        |> Seq.sortBy (fun (_, t) -> t.ToInstant())
                        |> Seq.map (fun (n, _) -> n)
                        |> Seq.toArray

                    let downloadAndDeserialize ct blobName =
                        let client = captureContainer.GetBlobClient(blobName = blobName)
                        let downloadInfo = client.Download(ct)
                        downloadInfo.Value.Content
                        |> ReadEventDataFromAvroStream blobName

                    for blobName in blobs do
                        eprintfn "Reading %s" blobName
                        let toEvent = createFromEventHubCapture convert partitionId blobName

                        yield!
                            blobName
                            |> downloadAndDeserialize cancellationToken
                            |> Seq.map toEvent
                            |> Seq.choose id
               }

    [<Extension>]
    let ReadAllEvents<'TEvent> (connections: MeteringConnections) (convert: Func<EventData,'TEvent>) (partitionId: PartitionID) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        connections |> readAllEvents (FuncConvert.FromFunc(convert)) partitionId cancellationToken

    let readEventsFromPosition<'TEvent> (convert: EventData -> 'TEvent) (mp: MessagePosition) (cancellationToken: CancellationToken) (connections: MeteringConnections) : IEnumerable<EventHubEvent<'TEvent>> =
        match connections.EventHubConfig.CaptureStorage with
        | None -> Seq.empty
        | Some { CaptureFileNameFormat = captureFileNameFormat; Storage = captureContainer } ->
            seq {
                let ehInfo = (connections.EventHubConfig.EventHubName, mp.PartitionID)
                let getTime = extractTime captureFileNameFormat ehInfo
                let blobNames = getCaptureBlobs cancellationToken mp.PartitionID connections
                let (startTime, sequenceNumber) = (mp.PartitionTimestamp, mp.SequenceNumber)
                let fullyRelevant (blobName: string, timeStampBlob: MeteringDateTime) =
                    timeStampBlob |> containsFullyRelevantEvents startTime

                let blobs =
                    blobNames
                    |> Seq.map (fun name -> (name, name |> getTime))
                    |> Seq.filter (fun (_, time) -> time.IsSome)
                    |> Seq.map (fun (name, time) -> (name, time.Value))
                    |> Seq.sortBy (fun (blob, time) -> time.ToInstant())
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

                let indexOfTheFirstFullyRelevantCaptureFileOption = blobs |> Array.tryFindIndex fullyRelevant

                let indexOfTheFirstPartlyRelevantCaptureFile =
                    match indexOfTheFirstFullyRelevantCaptureFileOption with
                    | None -> blobs.Length - 1
                    | Some indexOfTheFirstFullyRelevantCaptureFile -> indexOfTheFirstFullyRelevantCaptureFile - 1

                let relevantBlobs =
                    blobs
                    |> Array.skip indexOfTheFirstPartlyRelevantCaptureFile
                    |> Array.map (fun (n,t) -> n)

                let downloadAndDeserialize ct blobName =
                    let client = captureContainer.GetBlobClient(blobName = blobName)
                    let downloadInfo = client.Download(ct)
                    downloadInfo.Value.Content
                    |> ReadEventDataFromAvroStream blobName

                let isRelevantEvent (sn: SequenceNumber) (e: EventHubEvent<'TEvent>) =
                    // only emit events after the sequence number we already have processed
                    sn < e.MessagePosition.SequenceNumber

                for blobName in relevantBlobs do
                    let toEvent = createFromEventHubCapture convert mp.PartitionID blobName

                    yield!
                        blobName
                        |> downloadAndDeserialize cancellationToken
                        |> Seq.map toEvent
                        |> Seq.choose id
                        |> Seq.filter (isRelevantEvent sequenceNumber)
            }

    [<Extension>]
    let ReadEventsFromPosition<'TEvent> (connections: MeteringConnections) (convert: Func<EventData,'TEvent>) (mp: MessagePosition) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        connections |> readEventsFromPosition (FuncConvert.FromFunc(convert)) mp cancellationToken

    let readEventsFromTime<'TEvent> (convert: EventData -> 'TEvent) (partitionId: PartitionID) (startTime: MeteringDateTime) (cancellationToken: CancellationToken) (connections: MeteringConnections) : IEnumerable<EventHubEvent<'TEvent>> =
        match connections.EventHubConfig.CaptureStorage with
        | None -> Seq.empty
        | Some { CaptureFileNameFormat = captureFileNameFormat; Storage = captureContainer } ->
            seq {
                let ehInfo = (connections.EventHubConfig.EventHubName, partitionId)
                let getTime = extractTime captureFileNameFormat ehInfo
                let blobNames = getCaptureBlobs cancellationToken partitionId connections
                let fullyRelevant (blobName: string, timeStampBlob: MeteringDateTime) =
                    timeStampBlob |> containsFullyRelevantEvents startTime

                let blobs =
                    blobNames
                    |> Seq.map (fun name -> (name, name |> getTime))
                    |> Seq.filter (fun (_, time) -> time.IsSome)
                    |> Seq.map (fun (name, time) -> (name, time.Value))
                    |> Seq.sortBy (fun (blob, time) -> time.ToInstant())
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

                let indexOfTheFirstFullyRelevantCaptureFileOption = blobs |> Array.tryFindIndex fullyRelevant

                let indexOfTheFirstPartlyRelevantCaptureFile =
                    match indexOfTheFirstFullyRelevantCaptureFileOption with
                    | None -> blobs.Length - 1
                    | Some indexOfTheFirstFullyRelevantCaptureFile -> indexOfTheFirstFullyRelevantCaptureFile - 1

                let relevantBlobs =
                    blobs
                    |> Array.skip indexOfTheFirstPartlyRelevantCaptureFile
                    |> Array.map (fun (n,t) -> n)

                let downloadAndDeserialize ct blobName  =
                    let client = captureContainer.GetBlobClient(blobName = blobName)
                    let downloadInfo = client.Download(ct)
                    downloadInfo.Value.Content
                    |> ReadEventDataFromAvroStream blobName

                let isRelevantEvent (start: MeteringDateTime) (e: EventHubEvent<'TEvent>) =
                    let st = start.ToInstant()
                    // only emit events after the sequence number we already have processed
                    st < e.MessagePosition.PartitionTimestamp.ToInstant()

                for blobName in relevantBlobs do
                    let toEvent = createFromEventHubCapture convert partitionId blobName

                    yield!
                        blobName
                        |> downloadAndDeserialize cancellationToken
                        |> Seq.map toEvent
                        |> Seq.choose id
                        |> Seq.filter (isRelevantEvent startTime)
            }
    [<Extension>]
    let bytesToMeteringUpdateEvent (bytes: byte array) : MeteringUpdateEvent =
        try
            //Encoding.UTF8.GetString(bytes) |> OK

            let ms = (new MemoryStream(bytes))
            let reader = (new StreamReader(ms))
            reader.ReadToEnd() |> Ok
        with
        | _ ->
            bytes
            |> UnprocessableByteContent
            |> UnprocessableMessage
            |> Error
        |> function
            | Ok str ->
                try
                    let x = str |> Json.fromStr2<MeteringUpdateEvent>

                    match x with
                    | Ok v -> v
                    | Error _ -> str |> UnprocessableStringContent |> UnprocessableMessage
                with
                | _ -> str |> UnprocessableStringContent |> UnprocessableMessage
            | Error e -> e

    [<Extension>]
    let toMeteringUpdateEvent (eventData: EventData) : MeteringUpdateEvent =
        eventData.Body.ToArray() |> bytesToMeteringUpdateEvent

