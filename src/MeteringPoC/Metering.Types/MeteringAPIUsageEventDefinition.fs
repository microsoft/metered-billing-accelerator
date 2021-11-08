namespace Metering.Types

open System 

type MeteringAPIUsageEventDefinition = // From aggregator to metering API
    { ResourceId: InternalResourceId
      Quantity: decimal 
      PlanId: PlanId
      DimensionId: DimensionId 
      EffectiveStartTime: MeteringDateTime }

type MarketplaceSubmissionAcceptedResponse = 
    { ResourceID: InternalResourceId  
      Quantity: decimal 
      PlanId: PlanId
      DimensionId: DimensionId 
      EffectiveStartTime: MeteringDateTime 
      
      UsageEventId: string
      MessageTime: MeteringDateTime 
      ResourceURI: string }

type MarketplaceSubmissionError =
    | Duplicate of MarketplaceSubmissionAcceptedResponse
    | BadResourceId of MeteringAPIUsageEventDefinition
    | InvalidEffectiveStartTime of MeteringAPIUsageEventDefinition

type MarketplaceSubmissionResult = Result<MarketplaceSubmissionAcceptedResponse, MarketplaceSubmissionError>