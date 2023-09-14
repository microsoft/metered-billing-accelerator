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

let readAllEvents<'TEvent> (directory: string) (blobPrefix: string) (convert: EventData -> 'TEvent) (partitionId: PartitionID) (cancellationToken: CancellationToken) : IEnumerable<EventHubEvent<'TEvent>> =
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
            Directory.GetFiles(path = directory, searchPattern = "*.avro") // all avro files
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
                |> Seq.map (fun e -> EventHubEvent.createFromCapture (convert e) (RehydratedFromCaptureEventData.getMessagePosition e) None blobName )
    }

let recreateLatestState directory partitionId stateFile =
    File.WriteAllText(
        path = stateFile,
        contents =
            (readAllEvents<MeteringUpdateEvent>
                directory
                "whatever"
                CaptureProcessor.toMeteringUpdateEvent
                partitionId
                CancellationToken.None
            |> Seq.scan
                MeterCollectionLogic.handleMeteringEvent
                MeterCollection.Empty
            //|> Seq.map (fun state ->
            //    match state.LastUpdate with
            //    | None -> ()
            //    | Some mu ->
            //        if  (int mu.SequenceNumber) % 100 = 0
            //        then printfn "%d" (mu.SequenceNumber)
            //        else ()
            //    state
            //)
            |> Seq.last
            |> Json.toStr 2))

// Processing partion 0 consumed 1.0 GB, partition 1 consumed 3.5 GB, and partition 2 consumed 1.7 GB of memory
let directory = @"..\..\..\..\..\..\testcaptures"
let partitionId = PartitionID.create "0"
let stateFile = $"..\..\..\..\..\..\state{ partitionId.value }.json"

recreateLatestState directory partitionId stateFile
