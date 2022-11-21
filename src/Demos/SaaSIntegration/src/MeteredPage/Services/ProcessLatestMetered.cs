namespace MeteredPage.Services;

using Azure.Storage.Blobs;
using MeteredPage.ViewModels;
using Metering.BaseTypes;
using Metering.Integration;
using Metering.Utils;
using Microsoft.Extensions.Configuration;
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
        var config = MeteringConnections.getFromEnvironment();
        var result = await ManagementUtils.getMetersForSubscription(config, MarketplaceResourceId.fromStr(subscriptionId));
        s
        // TODO There is a hard-coded partition ID
        int hardCodedPartitionIdMustBeChanged = 0;
        string latest =  $"{eventHubNameSpace}.servicebus.windows.net/{eventHubName}/{hardCodedPartitionIdMustBeChanged}/latest.json.gz";
        BlobServiceClient blobServiceClient = new(connectionString: storageConnectionString);
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(blobContainerName: "snapshots");
        BlobClient blobClient = containerClient.GetBlobClient(latest);
        string downloadFilePath = $"{downloadlocation}latest.json.gz";
        string DecompressedFileName = $"{downloadlocation}latest.json";
        await blobClient.DownloadToAsync(downloadFilePath);

        using FileStream compressedFileStream = File.Open(downloadFilePath, FileMode.Open);
        using FileStream outputFileStream = File.Create(DecompressedFileName);
        using GZipStream decompressor = new(compressedFileStream, CompressionMode.Decompress);
        decompressor.CopyTo(outputFileStream);
        outputFileStream.Close();

        string jsonString = await File.ReadAllTextAsync(DecompressedFileName);
        var metercollections = Json.fromStr<MeterCollection>(jsonString);

        CustomerMetersModel currentMeters = new(
            subscriptionId, 
            metercollections.LastUpdate.Value.PartitionTimestamp.ToString(),
            CurrentToBeReported: new(), 
            CurrentMeterSummary: new());

        foreach (Meter meter in metercollections.Meters)
        {
            if (currentMeters.SubscriptionId == meter.Subscription.MarketplaceResourceId.ResourceId())
            {
                foreach (var meterKey in meter.Subscription.Plan.BillingDimensions.value)
                {
                    MeterModels meterSummary = new();
                    if (meterKey.Value.IsSimpleConsumptionBillingDimension && ((SimpleConsumptionBillingDimension)meterKey).Meter.Value.IsConsumedQuantity)
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
                if (currentMeters.SubscriptionId == marketplace.MarketplaceResourceId.ToString())
                {
                    currentMeters.CurrentToBeReported.Add(new ToBeReportedModel()
                    {
                        PlanId = marketplace.PlanId.value,
                        DimensionId = marketplace.DimensionId.value,
                        Quantity = marketplace.Quantity.ToString(),
                        EffectiveStartTime = marketplace.EffectiveStartTime.ToString()
                    });
                }
            }
        }

        return currentMeters;
    }
}