namespace MeteredPage.Services;

using MeteredPage.ViewModels;
using Metering.BaseTypes;
using Metering.Integration;
using Metering.Utils;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class ProcessLatestMetered
{
    private readonly MeteringConnections connections;

    public ProcessLatestMetered (IConfiguration configuration)
    {
        this.connections = MeteringConnections.getFromEnvironment();
    }

    public async Task<CustomerMetersModel> GetLatestMetered(string subscriptionId)
    {
        var reportingOverviews = await ManagementUtils.getMetersForSubscription(
            connections,
            MarketplaceResourceId.fromStr(subscriptionId),
            cancellationToken: CancellationToken.None);

        var meterSummary = reportingOverviews
            .Select(x => new MeterSummaryModel(
                Name: x.ApplicationInternalName,
                LastUpdate: x.LastUpdate.ToString(),
                ConsumedDimensionQuantity: x.TotalConsumed.ToString(),
                IncludedDimensionQuantity: x.RemainingIncluded.ToString()))
            .ToList();

        return new(
            SubscriptionId: subscriptionId,
            CurrentMeterSummary: meterSummary);
    }
}