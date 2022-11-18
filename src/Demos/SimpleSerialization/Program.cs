
using Metering.BaseTypes;

string meterCollectionAsJson = File.ReadAllText(@"..\..\..\..\..\Metering.Tests\data\state.json");
MeterCollection meterCollection = Json.fromStr<MeterCollection>(meterCollectionAsJson);
foreach (Meter meter in meterCollection.Meters)
{
    MarketplaceResourceId meterResourceId = meter.Subscription.MarketplaceResourceId;

    foreach (var kv in meter.Subscription.Plan.BillingDimensions)
    {
        var name = kv.Key;
        var dimension = kv.Value;

        Console.WriteLine($"{meterResourceId}: {name} - {dimension}");
    }
}
