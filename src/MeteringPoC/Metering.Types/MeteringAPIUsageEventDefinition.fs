namespace Metering.Types

type MeteringAPIUsageEventDefinition = // From aggregator to metering API
    { ResourceId: InternalResourceId
      Quantity: decimal 
      PlanId: PlanId
      DimensionId: DimensionId 
      EffectiveStartTime: MeteringDateTime }

module MeteringAPIUsageEventDefinition =
    let toStr (x: MeteringAPIUsageEventDefinition) : string =
        $"Usage: {x.ResourceId |> InternalResourceId.toStr} {x.EffectiveStartTime} {x.PlanId}/{x.DimensionId}: {x.Quantity}"

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
    | Duplicate of string
    | BadResourceId of string
    | InvalidEffectiveStartTime of string
    | CommunicationsProblem of string

type AzureHttpResponseHeaders =
    { /// The `x-ms-requestid` HTTP header.
      RequestID: string 

      /// The `x-ms-correlationid` HTTP header
      CorrelationID: string }

type MarketplaceSubmissionResult = 
    { Payload: MeteringAPIUsageEventDefinition
      Headers: AzureHttpResponseHeaders
      Result: Result<MarketplaceSubmissionAcceptedResponse, MarketplaceSubmissionError> }
    
module MarketplaceSubmissionResult =
    let toStr (x: MarketplaceSubmissionResult) : string =
        match x.Result with
        | Ok x -> $"{x.EffectiveStartTime |> MeteringDateTime.toStr}: Usage submitted: {x.ResourceId} {x.PlanId |> PlanId.value}/{x.DimensionId |> DimensionId.value}={x.Quantity |> Quantity.valueAsFloat}"
        | Error e -> 
            match e with
            | Duplicate _ -> $"Duplicate: {x.Payload.EffectiveStartTime} {x.Payload.ResourceId}"
            | BadResourceId _ -> $"Bad resource ID: {x.Payload.ResourceId}" 
            | InvalidEffectiveStartTime _ -> $"InvalidEffectiveStartTime: {x.Payload.EffectiveStartTime} {x.Payload.ResourceId}"
            | CommunicationsProblem msg -> "Something bad happened: {msg}"
