namespace Metering.Types

type MeteringAPIUsageEventDefinition = // From aggregator to metering API
    { ResourceId: InternalResourceId
      Quantity: decimal 
      PlanId: PlanId
      DimensionId: DimensionId 
      EffectiveStartTime: MeteringDateTime }

type MarketplaceSubmissionAcceptedResponse = 
    { UsageEventId: string
      MessageTime: MeteringDateTime
      Status: string
      ResourceId: string
      ResourceURI: string
      Quantity: Quantity
      DimensionId: DimensionId
      EffectiveStartTime: MeteringDateTime
      PlanId: PlanId }

type MarketplaceSubmissionError =
    | Duplicate
    | BadResourceId
    | InvalidEffectiveStartTime
    | CommunicationsProblem of string

type MarketplaceSubmissionResult = 
    { Payload: MeteringAPIUsageEventDefinition 
      Result: Result<MarketplaceSubmissionAcceptedResponse, MarketplaceSubmissionError> }
    