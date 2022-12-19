// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Integration 

open System
open System.Collections.Generic
open System.IO
open System.IO.Compression
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open Azure
open Azure.Storage.Blobs
open Azure.Storage.Blobs.Models
open Azure.Storage.Blobs.Specialized
open Azure.Storage.Sas
open Metering.BaseTypes
open Metering.BaseTypes.EventHub

[<Extension>]
module MeterCollectionStore =
    open MeterCollectionLogic
    
    type SnapshotName = 
        { EventHubName: EventHubName
          MessagePosition : MessagePosition }

    let private toUTF8String (bytes: byte[]) : string = Encoding.UTF8.GetString(bytes)

    let private toUTF8Bytes (str: string) : byte[] = Encoding.UTF8.GetBytes(str)

    let private gzipCompress (input: Stream) : Stream =
        use output = new MemoryStream()        
        using (new GZipStream(output, CompressionMode.Compress)) (fun gzip -> input.CopyTo(gzip))
        new MemoryStream(output.ToArray())
    
    let private gzipDecompress (input: Stream) : Stream =
        use output = new MemoryStream()
        using (new GZipStream(input, CompressionMode.Decompress)) (fun gzip -> gzip.CopyTo(output))
        new MemoryStream(output.ToArray())

    let private asJSONStream (t: 'T) : Stream =
        t
        |> Json.toStr 0
        |> toUTF8Bytes
        |> (fun x -> new MemoryStream(x))

    let private fromJSONStream<'T> (stream: Stream) : Task<'T> =
        task {
            use ms = new MemoryStream()
            let! _ = stream.CopyToAsync(ms)
            return ms.ToArray() |> toUTF8String |> Json.fromStr<'T>
        }

    /// Copies the source blob to the destination
    let private CopyBlobAsync
        (source: BlobClient)
        (destination: BlobClient)
        ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken)
        =
        task {
            let now = DateTimeOffset.UtcNow
            let aMinuteAgo = now.AddMinutes(-1.0)
            let inTenMinutes = now.AddMinutes(10.0)
            let oneDay = now.AddDays(1.0)

            let blobServiceClient =
                source
                    .GetParentBlobContainerClient()
                    .GetParentBlobServiceClient()

            // https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blob-user-delegation-sas-create-dotnet
            let! userDelegationKey =
                blobServiceClient.GetUserDelegationKeyAsync(
                    startsOn = aMinuteAgo,
                    expiresOn = oneDay,
                    cancellationToken = cancellationToken
                )

            let sasBuilder =
                new BlobSasBuilder(
                    permissions = BlobContainerSasPermissions.Read, 
                    expiresOn = inTenMinutes, 
                    StartsOn = aMinuteAgo, 
                    Resource = "b", 
                    BlobContainerName = source.BlobContainerName, 
                    BlobName = source.Name) 

            let blobUriBuilder = new BlobUriBuilder(
                uri = source.Uri,
                Sas = sasBuilder.ToSasQueryParameters(userDelegationKey.Value, blobServiceClient.AccountName))

            let options = new BlobCopyFromUriOptions (DestinationConditions = new BlobRequestConditions())
                        
            let! _ =
                destination.SyncCopyFromUriAsync(
                    source = (blobUriBuilder.ToUri()),
                    options = options,
                    cancellationToken = cancellationToken
                )

            return ()
        }

    module Naming =        
        let private prefix (name: EventHubName) = $"{name.FullyQualifiedNamespace}/{name.InstanceName}"

        let private regexPattern = "(?<ns>[^\.]+?)\.servicebus\.windows\.net/(?<hub>[^\/]+?)/(?<partitionid>[^\/]+?)/(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})--(?<hour>\d{2})-(?<minute>\d{2})-(?<second>\d{2})---sequencenr-(?<sequencenr>\d+)\.json\.gz" // " // (?<year>\d{4})
    
        let internal latestName (meteringConnections: MeteringConnections) (partitionId: PartitionID) =
            $"{meteringConnections.EventHubConfig.EventHubName |> prefix}/{partitionId.value}/latest.json.gz"
    
        let internal currentName (meteringConnections: MeteringConnections) (lastUpdate: MessagePosition) =
            $"{meteringConnections.EventHubConfig.EventHubName |> prefix}/{lastUpdate.PartitionID.value}/{lastUpdate.PartitionTimestamp |> MeteringDateTime.blobName}---sequencenr-{lastUpdate.SequenceNumber}.json.gz"
    
        let blobnameToPosition config blobName = 
            let i32 (m: Match) (name: string) = 
                match m.Groups.ContainsKey name with
                | false -> None
                | true ->
                    match m.Groups[name].Value |> Int32.TryParse with
                    | false, _ -> None
                    | _, v -> Some v
            let sn (m: Match) (name: string) = 
                match m.Groups.ContainsKey name with
                | false -> None
                | true ->
                    match m.Groups[name].Value |> SequenceNumber.TryParse with
                    | false, _ -> None
                    | _, v -> Some v
            let s (m: Match) (name: string) = 
                match m.Groups.ContainsKey name with
                | false -> None
                | true -> Some m.Groups[name].Value

            let regex = new Regex(pattern = regexPattern, options = RegexOptions.ExplicitCapture)
            let m = regex.Match(input = blobName)
            
            match ("ns" |> s m), ("hub" |> s m), ("partitionid" |> s m), ("year" |> i32 m), ("month" |> i32 m), ("day" |> i32 m), ("hour" |> i32 m), ("minute" |> i32 m), ("second" |> i32 m), ("sequencenr" |> sn m) with
            | Some ns, Some hub, Some partitionId, Some y, Some m, Some d, Some H, Some M, Some S, Some sequenceNumber -> 
                { EventHubName = 
                    EventHubName.create ns hub
                  MessagePosition = 
                    (MessagePosition.create 
                        partitionId 
                        sequenceNumber 
                        (MeteringDateTime.create y m d H M S)) } |> Some
            | _ -> None

    [<Extension>]
    let loadStateFromFilename
        (meteringConnections: MeteringConnections)
        (partitionID: PartitionID)
        ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken)
        (name: string)
        : Task<MeterCollection option> =
        task {
            let blob = meteringConnections.SnapshotStorage.GetBlobClient(name)
    
            try
                let! content = blob.DownloadAsync(cancellationToken = cancellationToken)
                let! meterCollection = content.Value.Content |> gzipDecompress |> fromJSONStream<MeterCollection>
        
                return Some meterCollection
            with
            | :? RequestFailedException as rfe when rfe.ErrorCode = "BlobNotFound" ->
                return None
            | e -> 
                // TODO log some weird exception
                return None
        }

    [<Extension>]
    let loadStateFromPosition config partitionID cancellationToken messagePosition =
        Naming.currentName config messagePosition
        |> loadStateFromFilename config partitionID cancellationToken

    [<Extension>]
    let loadLastState (connections: MeteringConnections) partitionID cancellationToken =
        Naming.latestName connections partitionID
        |> loadStateFromFilename connections partitionID cancellationToken
    
    //let loadStateBySequenceNumber config partitionID cancellationToken (sequenceNumber: SequenceNumber) =
    //    let blobNames = CaptureProcessor.getCaptureBlobs 

    let private createBlobMetadata (lastUpdate) =
        let options = new BlobUploadOptions()
        options.Metadata <- new Dictionary<string,string>()
        options.Metadata.Add(
            key = "SequenceNumber", 
            value = lastUpdate.SequenceNumber.ToString())
        options.Metadata.Add(
            key = "PartitionId", 
            value = lastUpdate.PartitionID.value)
        options.Metadata.Add(
            key = "PartitionTimestamp", 
            value = (lastUpdate.PartitionTimestamp |> MeteringDateTime.toStr))
        options

    let storeLastState
        (connections: MeteringConnections)
        (meterCollection: MeterCollection)
        ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken)
        : Task =

        match meterCollection |> Some |> lastUpdate with
        | None -> Task.FromResult "Empty collection, skipped saving"
        | Some lastUpdate ->
            // let name = meterCollection |> getLastUpdateAsString
            let current = Naming.currentName connections lastUpdate
            let latest = Naming.latestName connections lastUpdate.PartitionID

            task {
                let blobDate = connections.SnapshotStorage.GetBlobClient(current)
                
                //let! exists = blobDate.ExistsAsync(cancellationToken = cancellationToken)

                //if exists.Value then
                //    eprintfn $"Already existed {name}"
                //else
                use stream = meterCollection |> asJSONStream |> gzipCompress
                let options = lastUpdate |> createBlobMetadata

                try
                    //let! _ = blobDate.UploadAsync(content = stream, overwrite = true, cancellationToken = cancellationToken)
                    let! _ = blobDate.UploadAsync(
                        content = stream, 
                        options = options, 
                        cancellationToken = cancellationToken )

                    ()
                with
                | :? RequestFailedException as rfe when rfe.ErrorCode = "BlobAlreadyExists" -> 
                    ()

                try
                    ignore <| stream.Seek(offset = 0L, origin = SeekOrigin.Begin)
                    let latestBlob = connections.SnapshotStorage.GetBlobClient(latest)
                    let! s = latestBlob.UploadAsync(
                        content = stream, 
                        options = options, 
                        cancellationToken = cancellationToken)
                    ()
                with
                | :? RequestFailedException as rfe when rfe.ErrorCode = "BlobAlreadyExists" -> 
                    ()

                //try
                //    let latestBlob =
                //        connections.SnapshotStorage.GetBlobClient(latest)
                //    let! _ = latestBlob.DeleteAsync(cancellationToken = cancellationToken)
                //    let! _ = CopyBlobAsync blobDate latestBlob cancellationToken
                //with
                //| e -> eprintfn $"Delete/Copy problem {e.Message}"
            }

    let isLoaded<'T> (state: 'T option) : bool = 
        state.IsSome
