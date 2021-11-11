namespace Metering.Types

open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices
open Azure
open Azure.Storage.Blobs
open Azure.Storage.Blobs.Specialized
open Azure.Storage.Sas
open Metering.Types.EventHub

module Aggregator =
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

    let LoadLastState
        (snapshotContainerClient: BlobContainerClient)
        (partitionID: PartitionID)
        ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken)
        : Task<MeterCollection> =
        task {
            let blobName = $"partition-{partitionID}/latest.json"

            let blob =
                snapshotContainerClient.GetBlobClient(blobName)

            let! content = blob.DownloadContentAsync(cancellationToken = cancellationToken)

            return
                content.Value.Content
                |> (fun x -> x.ToArray())
                |> (fun x -> Encoding.UTF8.GetString(x))
                |> Json.fromStr<MeterCollection>
        }


    let StoreLastState
        (snapshotContainerClient: BlobContainerClient)
        ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken)
        (meterCollection: MeterCollection)
        : Task =
        match meterCollection |> MeterCollection.lastUpdate with
        | None -> Task.CompletedTask
        | Some lastUpdate ->
            task {
                let timestamp =
                    lastUpdate.PartitionTimestamp
                    |> MeteringDateTime.blobName

                let blobDateName =
                    $"partition-{lastUpdate.PartitionID}/{timestamp}---sequencenr-{lastUpdate.SequenceNumber}.json"

                let blobCopyName =
                    $"partition-{lastUpdate.PartitionID}/latest.json"

                use stream =
                    meterCollection
                    |> Json.toStr
                    |> (fun x -> Encoding.UTF8.GetBytes(x))
                    |> (fun x -> new MemoryStream(x))

                let blobDate =
                    snapshotContainerClient.GetBlobClient(blobDateName)

                let! exists = blobDate.ExistsAsync(cancellationToken = cancellationToken)

                let! _ =
                    if not exists.Value then
                        try
                            blobDate.UploadAsync(content = stream, cancellationToken = cancellationToken)
                        with
                        | :? RequestFailedException as rfe when rfe.ErrorCode = "BlobAlreadyExists" ->
                            Task.FromResult(null)
                    else
                        Task.FromResult(null)

                let latestBlob =
                    snapshotContainerClient.GetBlobClient(blobCopyName)

                let! _ = CopyBlobAsync blobDate latestBlob cancellationToken

                return ()
            }
