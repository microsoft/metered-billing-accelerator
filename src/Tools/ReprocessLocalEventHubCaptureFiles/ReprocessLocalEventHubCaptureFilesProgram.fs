// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

open System
open System.Collections.Generic
open System.IO
open System.Threading
open Metering.BaseTypes
open Metering.BaseTypes.EventHub
open Metering.EventHub
open Azure.Messaging.EventHubs
open System.Text.RegularExpressions

//
// This utility reads the complete EventHub capture for a specific partition (all events since beginning of time), from a local directory,
// and stores the events and the state after applying a certain event, as JSON files in the file system.
//

let readAllEvents<'TEvent> (captureDirectory: string) (blobPrefix: string) (convert: EventData -> 'TEvent) (partitionId: PartitionID) (cancellationToken: CancellationToken) : IEnumerable<EventHubEvent<'TEvent>> =
    seq {
        // p0--2023-08-22--19-57-36.avro
        let extractTime (captureFileNameFormat: string) (partitionId: PartitionID) (blobName: string) : MeteringDateTime option =
            let createRegex =
                let createRegexPattern (captureFileNameFormat: string) (partitionId: PartitionID) : string =
                    $"{captureFileNameFormat}.avro"
                        .Replace("{PartitionId}", partitionId.value)
                        .Replace("{Year}", "(?<year>\d{4})")
                        .Replace("{Month}", "(?<month>\d{2})")
                        .Replace("{Day}", "(?<day>\d{2})")
                        .Replace("{Hour}", "(?<hour>\d{2})")
                        .Replace("{Minute}", "(?<minute>\d{2})")
                        .Replace("{Second}", "(?<second>\d{2})")

                createRegexPattern captureFileNameFormat

            let regex = new Regex(
                pattern = createRegex partitionId,
                options = RegexOptions.ExplicitCapture)

            let matcH = regex.Match(input = blobName)
            match matcH.Success with
            | false -> None
            | true ->
                let g (name: string) = matcH.Groups[name].Value |> Int32.Parse
                MeteringDateTime.create ("year" |> g) ("month" |> g) ("day" |> g) ("hour" |> g) ("minute" |> g) ("second" |> g)
                |> Some

        let time = extractTime "p{PartitionId}--{Year}-{Month}-{Day}--{Hour}-{Minute}-{Second}" partitionId

        let files =
            Directory.GetFiles(path = captureDirectory, searchPattern = "*.avro") // all avro files
            |> Array.map (fun filename -> (filename, time filename)) // extract time from filename
            |> Array.choose (fun (filename, time) -> time |> Option.map (fun t -> (filename, t))) // filter out files without time
            |> Seq.sortBy (fun (_, time) -> time.LocalDateTime) // sort by time

        for (filename, _time) in files do
            let blobName = blobPrefix + "/" + filename
            use stream =
                File.OpenRead(filename)

            yield!
                stream
                |> CaptureProcessor.ReadEventDataFromAvroStream blobName
                |> Seq.map (fun e -> EventHubEvent.createFromCapture (convert e) (RehydratedFromCaptureEventData.getMessagePosition partitionId e) None blobName )
    }

let recreateLatestState captureDirectory partitionId stateFile =
    let lastState =
        readAllEvents<MeteringUpdateEvent>
                captureDirectory
                "whatever"
                CaptureProcessor.toMeteringUpdateEvent
                partitionId
                CancellationToken.None
        |> Seq.scan
            MeterCollectionLogic.handleMeteringEvent
            MeterCollection.Empty
        |> Seq.map (fun state ->
            match state.LastUpdate with
            | None -> ()
            | Some mu ->
                if  (int mu.SequenceNumber) % 1000 = 0
                then printf "%d " (mu.SequenceNumber)
                else ()
            state
        )
        |> Seq.last

    File.WriteAllText(
        path = stateFile,
        contents = (lastState |> Json.toStr 0))

    printfn "Created state file %s" (new FileInfo(stateFile)).FullName

    lastState

let expandCaptureToEventsInIndividualJsonFiles captureDirectory partitionId eventDirectory =
    if Directory.Exists(eventDirectory)
    then
        Directory.CreateDirectory(path = eventDirectory) |> ignore

    readAllEvents<MeteringUpdateEvent>
                captureDirectory
                "whatever"
                CaptureProcessor.toMeteringUpdateEvent
                partitionId
                CancellationToken.None
    |> Seq.filter (fun e ->
        // Skip UsageReported and Ping events
        match e.EventData with
        | UsageReported _ -> false
        | Ping _ -> false
        | _ -> true)
    |> Seq.iter(fun e ->
        let filename = $"{eventDirectory}\\p{e.MessagePosition.PartitionID.value}--{e.MessagePosition.SequenceNumber}--{e.MessagePosition.PartitionTimestamp |> MeteringDateTime.blobName}--{e.EventData.MessageType}.json"
        let content = e.EventData |> Json.toStr 2

        File.WriteAllText(
            path = filename,
            contents = content)
    )

/// Print the sequence numbers of all events that do not have a partition key
let printEventsWithoutPartitionKey captureDirectory partitionId =
    readAllEvents<MeteringUpdateEvent>
                captureDirectory
                "whatever"
                CaptureProcessor.toMeteringUpdateEvent
                partitionId
                CancellationToken.None
    |> Seq.filter (fun e -> e.MessagePosition.PartitionID.value = null)
    |> Seq.iter(fun e ->
        printf "%d " e.MessagePosition.SequenceNumber
    )

/// Check if there are any events that do have a partition key
let oneOrMoreEventsHaveAPartitionID (captureDirectory: string) (partitionId: PartitionID) : bool =
    readAllEvents<MeteringUpdateEvent>
                captureDirectory
                "whatever"
                CaptureProcessor.toMeteringUpdateEvent
                partitionId
                CancellationToken.None
    |> Seq.exists (fun e -> e.MessagePosition.PartitionID.value <> null)

let numberOfMessages (captureDirectory: string) (partitionId: PartitionID) : int =
    readAllEvents<MeteringUpdateEvent>
                captureDirectory
                "whatever"
                CaptureProcessor.toMeteringUpdateEvent
                partitionId
                CancellationToken.None
    |> Seq.length

/// Retrieve all unique marketplace resource IDs from the capture
let marketplaceResourceIDs (captureDirectory: string) (partitionId: PartitionID)  =
    readAllEvents<MeteringUpdateEvent>
                captureDirectory
                "whatever"
                CaptureProcessor.toMeteringUpdateEvent
                partitionId
                CancellationToken.None
    |> Seq.choose (fun e -> e.EventData.MarketplaceResourceID)
    |> Seq.distinct
    |> Seq.sort

let numberOfUnprocessedMessagesInStateFile (stateFile: string) =
    File.ReadAllText(stateFile)
    |> Json.fromStr<MeterCollection>
    |> (fun state -> state.UnprocessableMessages)
    |> List.length




let numberOfPartitions = 3
let captureDirectory = @"..\..\..\..\..\..\testcaptures" // This folder isn't checked in, so you need to create it yourself

[ 0 .. numberOfPartitions - 1 ]
|> Seq.map(sprintf "%d")
|> Seq.map(PartitionID.create)
|> Seq.iter(fun partitionId ->

    //
    // The code snippets below are independent of each other, so you can comment out the ones you don't need
    // Also, they iterate over all Avro files in the capture directory (again and again), so it's slightly inefficient
    //
    printfn $"Processing partition {partitionId.value}"

    // Processing partion 0 consumed 1.0 GB, partition 1 consumed 3.5 GB, and partition 2 consumed 1.7 GB of memory

    // Start with an empty state and apply all events, to get the latest state
    let stateFile = $"{captureDirectory}\state\state-p{ partitionId.value }.json"

    let lastState =
        recreateLatestState captureDirectory partitionId stateFile

    // Print the number of unprocessed messages in the state file
    lastState
    |> (fun state -> state.UnprocessableMessages)
    |> List.length
    |> printfn "Statefile %s has %d unprocessed messages" stateFile

    //printfn "Statefile %s has %d unprocessed messages" stateFile (numberOfUnprocessedMessagesInStateFile stateFile)

    //// Expand the capture to individual JSON files, one for each event
    //expandCaptureToEventsInIndividualJsonFiles
    //    captureDirectory
    //    partitionId
    //    $"{captureDirectory}\events\{partitionId.value}"

    //// Print the sequence numbers of all events that do not have a partition key
    printEventsWithoutPartitionKey
        captureDirectory
        partitionId

    //// Check if there are any events that do not have a partition key
    if oneOrMoreEventsHaveAPartitionID captureDirectory partitionId
    then printfn $"Partition {partitionId.value} has some events that *DO* have a partition key"
    else printfn $"No event in partition {partitionId.value} has a partition key"

    // printfn $"Partition {partitionId.value} has {numberOfMessages captureDirectory partitionId} messages"

    // Print all marketplace resource IDs by partition
    marketplaceResourceIDs captureDirectory partitionId
    |> Seq.map (fun id ->
        printfn $"Partition {partitionId.value} has marketplace resource ID {id.ToString()}"
        id
    )
    |> Seq.length
    |> (fun num ->
        printfn $"Partition {partitionId.value} has {num} different "
    )
)