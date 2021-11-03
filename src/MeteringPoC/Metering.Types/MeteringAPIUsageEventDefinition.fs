namespace Metering.Types

type MeteringAPIUsageEventDefinition = // From aggregator to metering API
    { ResourceId: ResourceID 
      SubscriptionType: SubscriptionType
      Quantity: decimal 
      PlanDimension: PlanDimension
      EffectiveStartTime: MeteringDateTime }