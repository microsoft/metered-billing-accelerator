namespace Metering.Types

type MeteringAPIUsageEventDefinition = // From aggregator to metering API
    { SubscriptionType: SubscriptionType
      Quantity: decimal 
      PlanDimension: PlanDimension
      EffectiveStartTime: MeteringDateTime }