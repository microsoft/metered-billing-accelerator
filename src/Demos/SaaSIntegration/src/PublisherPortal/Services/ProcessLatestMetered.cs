using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.IO.Compression;
using Metering.BaseTypes;
using PublisherPortal.ViewModels.Meter;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace PublisherPortal.Services
{
    public class ProcessLatestMetered
    {
        private string eventHubNameSpace;
        private string eventHubName;
        private string downloadlocation;
        private string storageConnectionString;


        public ProcessLatestMetered (IConfiguration configuration)
        {
            this.eventHubNameSpace = configuration["AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME"];
            this.eventHubName = configuration["AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME"];
            this.downloadlocation = configuration["Downloadlocation"];
            this.storageConnectionString = configuration["StorageConnectionString"];
        }

        public async Task<PublisherMetersModel> GetLatestMetered(string subscriptionId)
        {
            PublisherMetersModel currentPublisherMeters = new();

            string latest = eventHubNameSpace + ".servicebus.windows.net/" + eventHubName + "/0/latest.json.gz";
            BlobServiceClient blobServiceClient = new BlobServiceClient(storageConnectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("snapshots");
            BlobClient blobClient = containerClient.GetBlobClient(latest);
            string downloadFilePath = downloadlocation+"latest.json.gz";
            string DecompressedFileName = downloadlocation + "latest.json";
            await blobClient.DownloadToAsync(downloadFilePath);

            using FileStream compressedFileStream = File.Open(downloadFilePath, FileMode.Open);
            using FileStream outputFileStream = File.Create(DecompressedFileName);
            using var decompressor = new GZipStream(compressedFileStream, CompressionMode.Decompress);
            decompressor.CopyTo(outputFileStream);
            outputFileStream.Close();

            //sync version
            string jsonString = File.ReadAllText(DecompressedFileName);

            var metercollections = Json.fromStr<MeterCollection>(jsonString);

            // get Last Process Time
            currentPublisherMeters.lastProcessedMessage = metercollections.LastUpdate.Value.PartitionTimestamp.ToString();

            foreach (Meter meter in metercollections.Meters)
            {
                MeterSummaryModel currentMeters = new();
                
                foreach (KeyValuePair<DimensionId, MeterValue> meterKey in meter.CurrentMeterValues.value)
                {
                    MeterSummaryModel meterSummary = new()
                    {
                        SubscriptionId = meter.Subscription.MarketplaceResourceId.ResourceId()
                    };
                    if (meterKey.Value.IsConsumedQuantity)
                    {
                        meterSummary.DimensionName = meterKey.Key.value.ToString();
                        meterSummary.ConsumedDimensionQuantity = Convert.ToDecimal(meterKey.Value.ToString().Replace(" consumed", ""));
                    }
                    if (meterKey.Value.IsIncludedQuantity)
                    {
                        meterSummary.DimensionName = meterKey.Key.value.ToString();
                        meterSummary.IncludedDimensionQuantity = Convert.ToDecimal(meterKey.Value.ToString().Replace("Remaining ", ""));

                    }
                    currentPublisherMeters.CurrentMeterSummary.Add(meterSummary);
                }
            }

            // Get to be Reported
            if (metercollections.metersToBeSubmitted.Count() > 0)
            {
                foreach (var marketplace in metercollections.metersToBeSubmitted)
                {
                    var toBeReported = new ToBeReportedModel();

                        toBeReported.PlanId = marketplace.PlanId.ToString();
                        toBeReported.DimensionName = marketplace.DimensionId.ToString();
                        toBeReported.Quantity = Convert.ToDecimal(marketplace.Quantity.ToString());
                        toBeReported.EffectiveStartTime = marketplace.EffectiveStartTime.ToString();

                    currentPublisherMeters.CurrentToBeReported.Add(toBeReported);
                }
            }

            // get total summary by Dimension

            var summaryDimensionTotal = currentPublisherMeters.CurrentMeterSummary.GroupBy(t => t.DimensionName)
                           .Select(t => new DimensionTotalModel()
                           {
                               DimensionName = t.Key,
                               ConsumedDimensionQuantity = t.Sum(ta => ta.ConsumedDimensionQuantity),
                               IncludedDimensionQuantity = t.Sum(ta => ta.IncludedDimensionQuantity),
                           }).ToList();


            var summaryDimensionToBeReported = currentPublisherMeters.CurrentToBeReported.GroupBy(t => t.DimensionName)
                           .Select(t => new DimensionTotalModel()
                           {
                               DimensionName = t.Key,
                               ToBeProcess = t.Sum(ta => ta.Quantity)
                               
                           }).ToList();


            foreach( var meter in summaryDimensionToBeReported)
            {
                summaryDimensionTotal.Where(t => t.DimensionName == meter.DimensionName)
                    .ToList()
                    .ForEach(d => d.ToBeProcess = meter.ToBeProcess);
            }


            currentPublisherMeters.CurrentTotal = summaryDimensionTotal;
            return currentPublisherMeters;
        }
    }
}
