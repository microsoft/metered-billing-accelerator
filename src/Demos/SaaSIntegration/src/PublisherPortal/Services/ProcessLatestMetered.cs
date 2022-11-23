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
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Metering.Integration;
using Metering.Utils;
using System.Threading;

namespace PublisherPortal.Services
{
    public class ProcessLatestMetered
    {
        private readonly MeteringConnections connections;

        public ProcessLatestMetered(IConfiguration configuration)
        {
            this.connections = MeteringConnections.getFromEnvironment();
        }

        public async Task<PublisherMetersModel> GetLatestMetered(string )
        {
            var reportingOverviews = await ManagementUtils.getMetersForSubscription(
               connections,
               MarketplaceResourceId.fromStr(subscriptionId),
               cancellationToken: CancellationToken.None);

            PublisherMetersModel currentPublisherMeters = new();


            // get Last Process Time
            currentPublisherMeters.lastProcessedMessage = metercollections.LastUpdate.Value.PartitionTimestamp.ToString();

            foreach (Meter meter in metercollections.Meters)
            {
                MeterSummaryModel currentMeters = new();
                
                foreach (var meterKey in meter.CurrentMeterValues.value)
                {

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

                    MeterSummaryModel meterSummary = new(subscriptionId,)
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
