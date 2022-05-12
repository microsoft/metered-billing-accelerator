
using Metering.BaseTypes;

string meterCollectionAsJson = File.ReadAllText(@"..\..\..\..\..\Metering.Tests\data\state.json");
MeterCollection meterCollection = Json.fromStr<MeterCollection>(meterCollectionAsJson);
foreach (KeyValuePair<InternalResourceId, Meter> kvp in meterCollection.MeterCollection)
{
    InternalResourceId meterResourceId = kvp.Key;
    Meter meter = kvp.Value;

    foreach (KeyValuePair<DimensionId, MeterValue> meterKey in meter.CurrentMeterValues.value)
    {
        DimensionId dimensionId = meterKey.Key;
        MeterValue meterValue = meterKey.Value;

        Console.WriteLine($"{meterResourceId}: {dimensionId} - {meterValue}");
    }
}
