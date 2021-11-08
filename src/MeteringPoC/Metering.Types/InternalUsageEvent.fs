namespace Metering.Types

type InternalUsageEvent = // From app to aggregator
    { InternalResourceId: InternalResourceId
      Timestamp: MeteringDateTime  // timestamp from the sending app
      MeterName: ApplicationInternalMeterName
      Quantity: Quantity
      Properties: Map<string, string> option}
