using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.IO.Compression;
using Metering.BaseTypes;
using MeteredPage.ViewModels.Meter;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace MeteredPage.Services
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
        public async Task<CustomerMetersModel> GetLatestMetered(string subscriptionId)
        {
            CustomerMetersModel currentMeters = new CustomerMetersModel();
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


            currentMeters.SubscriptionId = subscriptionId;
            // get Last Process Time
            currentMeters.lastProcessedMessage = metercollections.LastUpdate.Value.PartitionTimestamp.ToString();

            foreach (KeyValuePair<InternalResourceId, Meter> kvp in metercollections.MeterCollection)
            {
                if (currentMeters.SubscriptionId == kvp.Key.ToString())
                {
                    Meter meter = kvp.Value;
                    foreach (KeyValuePair<DimensionId, MeterValue> meterKey in meter.CurrentMeterValues.value)
                    {
                        var meterSummary = new MeterSummaryModel();
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
            if (metercollections.metersToBeSubmitted.Count() > 0)
            {
                foreach (var marketplace in metercollections.metersToBeSubmitted)
                {
                    var toBeReported = new ToBeReportedModel();
                    if (currentMeters.SubscriptionId == marketplace.ResourceId.ToString())
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
}

