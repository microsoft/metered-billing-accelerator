
using Metering.BaseTypes;

string meterCollectionAsJson = File.ReadAllText(@"..\..\..\..\..\Metering.Tests\data\state.json");
MeterCollection meterCollection = Json.fromStr<MeterCollection>(meterCollectionAsJson);
foreach (Meter meter in meterCollection.Meters)
{
    MarketplaceResourceId meterResourceId = meter.Subscription.MarketplaceResourceId;

    foreach (KeyValuePair<ApplicationInternalMeterName, MeterValue> meterKey in meter.CurrentMeterValues.value)
    {
        var name = meterKey.Key;
        var meterValue = meterKey.Value;

        Console.WriteLine($"{meterResourceId}: {name} - {meterValue}");
    }
}
