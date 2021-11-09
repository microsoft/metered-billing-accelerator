namespace Metering.Types

type CurrentMeterValues = // Collects all meters per internal metering event type
    Map<DimensionId, MeterValue> 
