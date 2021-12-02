namespace Metering.Types

open System
open System.IO
open System.IO.Compression
open System.Text
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices
open Azure
open Azure.Storage.Blobs
open Azure.Storage.Blobs.Specialized
open Azure.Storage.Sas
open Metering.Types.EventHub

module MeterCollectionStore =
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
                new BlobSasBuilder(permissions = BlobContainerSasPermissions.Read, expiresOn = inTenMinutes)

            sasBuilder.StartsOn <- aMinuteAgo
            sasBuilder.Resource <- "b"
            sasBuilder.BlobContainerName <- source.BlobContainerName
            sasBuilder.BlobName <- source.Name

            let blobUriBuilder = new BlobUriBuilder(source.Uri)

            blobUriBuilder.Sas <-
                sasBuilder.ToSasQueryParameters(userDelegationKey.Value, blobServiceClient.AccountName)

            let! _ =
                destination.SyncCopyFromUriAsync(
                    source = (blobUriBuilder.ToUri()),
                    cancellationToken = cancellationToken
                )

            return ()
        }

    let private prefix (config: MeteringConfigurationProvider) = $"{config.MeteringConnections.EventProcessorClient.FullyQualifiedNamespace}/{config.MeteringConnections.EventProcessorClient.EventHubName}"
    
    let private latestName (config: MeteringConfigurationProvider) (partitionId: PartitionID) = $"{config |> prefix}/{partitionId |> PartitionID.value}/latest.json.gz"
    
    let private currentName (config: MeteringConfigurationProvider) (lastUpdate: MessagePosition) = $"{config |> prefix}/{lastUpdate.PartitionID |> PartitionID.value}/{lastUpdate.PartitionTimestamp |> MeteringDateTime.blobName}---sequencenr-{lastUpdate.SequenceNumber}.json.gz"
    
    let loadLastState
        (config: MeteringConfigurationProvider)
        (partitionID: PartitionID)
        ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken)
        : Task<MeterCollection option> =

        printfn $"Loading state for partition {partitionID |> PartitionID.value}"

        task {
            let latest = latestName config partitionID
            let blob = config.MeteringConnections.SnapshotStorage.GetBlobClient(latest)
            
            try
                let! content = blob.DownloadAsync(cancellationToken = cancellationToken)
                let! meterCollection = content.Value.Content |> gzipDecompress |> fromJSONStream<MeterCollection>
                
                eprintfn $"Successfully downloaded state, last event was {partitionID |> PartitionID.value}-{meterCollection |> MeterCollection.getLastUpdate}"
                return Some meterCollection
            with
            | :? RequestFailedException as rfe when rfe.ErrorCode = "BlobNotFound" ->
                eprintfn $"BlobNotFound for partition {partitionID |> PartitionID.value}"
                return Some MeterCollection.empty
            | e -> 
                // TODO log some weird exception
                eprintfn $"Bad stuff happening {e.Message}"
                return None
        }

    let storeLastState
        (config: MeteringConfigurationProvider)
        (meterCollection: MeterCollection)
        ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken)
        : Task =
        match meterCollection |> Some |> MeterCollection.lastUpdate with
        | None -> Task.CompletedTask
        | Some lastUpdate ->
            let current = currentName config lastUpdate
            let latest = latestName config lastUpdate.PartitionID

            task {
                let blobDate = config.MeteringConnections.SnapshotStorage.GetBlobClient(current)
                let! exists = blobDate.ExistsAsync(cancellationToken = cancellationToken)
                let! _ =
                    if not exists.Value then
                        try
                            use stream = meterCollection |> asJSONStream |> gzipCompress
                            blobDate.UploadAsync(content = stream, cancellationToken = cancellationToken)
                        with
                        | :? RequestFailedException as rfe when rfe.ErrorCode = "BlobAlreadyExists" ->
                            Task.FromResult(null)
                    else
                        Task.FromResult(null)

                let latestBlob =
                    config.MeteringConnections.SnapshotStorage.GetBlobClient(latest)

                let! _ = CopyBlobAsync blobDate latestBlob cancellationToken

                return ()
            }

    let isLoaded<'T> (state: 'T option) : bool = 
        state.IsSome
