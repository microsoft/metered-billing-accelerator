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
      ResourceURI: string }

type MarketplaceSubmissionError =
    | Duplicate
    | BadResourceId
    | InvalidEffectiveStartTime
    | CommunicationsProblem of exn

type MarketplaceSubmissionResult = 
    { Payload: MeteringAPIUsageEventDefinition 
      Result: Result<MarketplaceSubmissionAcceptedResponse, MarketplaceSubmissionError> }
    