
using Metering.BaseTypes;

string meterCollectionAsJson = File.ReadAllText(@"..\..\..\..\..\Metering.Tests\data\state.json");
MeterCollection meterCollection = Json.fromStr<MeterCollection>(meterCollectionAsJson);
foreach (Meter meter in meterCollection.MeterCollection)
{
    MarketplaceResourceId meterResourceId = meter.Subscription.MarketplaceResourceId;

    foreach (KeyValuePair<DimensionId, MeterValue> meterKey in meter.CurrentMeterValues.value)
    {
        DimensionId dimensionId = meterKey.Key;
        MeterValue meterValue = meterKey.Value;

        Console.WriteLine($"{meterResourceId}: {dimensionId} - {meterValue}");
    }
}
