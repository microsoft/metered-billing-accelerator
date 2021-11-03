namespace Metering.Types

type UsageSubmittedToAPIResult = // Once the metering API was called, either the metering submission successfully got through, or not (in which case we need to know which values haven't been submitted)
    { Payload: MeteringAPIUsageEventDefinition 
      Result: Result<unit, exn> }
