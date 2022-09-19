namespace MeteredPage.Services;

using Azure.Storage.Blobs;
using MeteredPage.ViewModels.Meter;
using Metering.BaseTypes;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

public class ProcessLatestMetered
{
    private readonly string eventHubNameSpace;
    private readonly string eventHubName;
    private readonly string downloadlocation;
    private readonly string storageConnectionString;

    public ProcessLatestMetered (IConfiguration configuration)
    {
        this.eventHubNameSpace = configuration["AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME"];
        this.eventHubName = configuration["AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME"];
        this.downloadlocation = configuration["Downloadlocation"];
        this.storageConnectionString = configuration["StorageConnectionString"];
    }

    public async Task<CustomerMetersModel> GetLatestMetered(string subscriptionId)
    {
        // TODO There is a hard-coded partition ID
        int hardCodedPartitionIdMustBeChanged = 0;
        string latest =  $"{eventHubNameSpace}.servicebus.windows.net/{eventHubName}/{hardCodedPartitionIdMustBeChanged}/latest.json.gz";
        BlobServiceClient blobServiceClient = new(storageConnectionString);
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("snapshots");
        BlobClient blobClient = containerClient.GetBlobClient(latest);
        string downloadFilePath = downloadlocation+"latest.json.gz";
        string DecompressedFileName = downloadlocation + "latest.json";
        await blobClient.DownloadToAsync(downloadFilePath);

        using FileStream compressedFileStream = File.Open(downloadFilePath, FileMode.Open);
        using FileStream outputFileStream = File.Create(DecompressedFileName);
        using GZipStream decompressor = new(compressedFileStream, CompressionMode.Decompress);
        decompressor.CopyTo(outputFileStream);
        outputFileStream.Close();

        string jsonString = await File.ReadAllTextAsync(DecompressedFileName);
        var metercollections = Json.fromStr<MeterCollection>(jsonString);

        CustomerMetersModel currentMeters = new()
        {
            SubscriptionId = subscriptionId,
            LastProcessedMessage = metercollections.LastUpdate.Value.PartitionTimestamp.ToString()
        };

        foreach (KeyValuePair<MarketplaceResourceId, Meter> kvp in metercollections.MeterCollection)
        {
            if (currentMeters.SubscriptionId == kvp.Key.ToString())
            {
                Meter meter = kvp.Value;
                foreach (KeyValuePair<DimensionId, MeterValue> meterKey in meter.CurrentMeterValues.value)
                {
                    MeterSummaryModel meterSummary = new();
                    if (meterKey.Value.IsConsumedQuantity)
                    {
                        meterSummary.DimensionName = meterKey.Key.value.ToString();
                        meterSummary.ConsumedDimensionQuantity = meterKey.Value.ToString().Replace(" consumed", "");
                    }
                    if (meterKey.Value.IsIncludedQuantity)
                    {
                        meterSummary.DimensionName = meterKey.Key.ToString();
                        meterSummary.IncludedDimensionQuantity = meterKey.Value.ToString().Replace("Remaining ", "");

                    }
                    currentMeters.CurrentMeterSummary.Add(meterSummary);
                }
            }
        }

        // Get to be Reported
        if (metercollections.metersToBeSubmitted.Any())
        {
            foreach (var marketplace in metercollections.metersToBeSubmitted)
            {
                var toBeReported = new ToBeReportedModel();
                if (currentMeters.SubscriptionId == marketplace.MarketplaceResourceId.ToString())
                {
                    toBeReported.PlanId = marketplace.PlanId.ToString();
                    toBeReported.DimensionId = marketplace.DimensionId.ToString();
                    toBeReported.Quantity = marketplace.Quantity.ToString();
                    toBeReported.EffectiveStartTime = marketplace.EffectiveStartTime.ToString();

                    currentMeters.CurrentToBeReported.Add(toBeReported);
                }
            }
        }

        return currentMeters;
    }
}