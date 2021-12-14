namespace Metering.Types

open System

type MeteringAPIUsageEventDefinition = // From aggregator to metering API
    { EffectiveStartTime: MeteringDateTime; PlanId: PlanId; DimensionId: DimensionId; Quantity: decimal; ResourceId: InternalResourceId }

module MeteringAPIUsageEventDefinition =
    let toStr (x: MeteringAPIUsageEventDefinition) : string =
        $"Usage: {x.ResourceId |> InternalResourceId.toStr} {x.EffectiveStartTime} {x.PlanId}/{x.DimensionId}: {x.Quantity}"

type BatchMeteringAPIUsageEventDefinition = 
    private Entries of MeteringAPIUsageEventDefinition list

module BatchMeteringAPIUsageEventDefinition =
    let create (x: MeteringAPIUsageEventDefinition seq) : BatchMeteringAPIUsageEventDefinition = 
        x
        |> Seq.toList
        |> (fun l -> 
            if l.Length > 25
            then raise (new ArgumentException(message = $"A maximum of 25 {nameof(MeteringAPIUsageEventDefinition)} item is allowed")) 
            else l)
        |> Entries

type MarketplaceSubmissionAcceptedResponse = 
    { UsageEventId: string
      MessageTime: MeteringDateTime
      Status: string      
      ResourceURI: string      
      EffectiveStartTime: MeteringDateTime; PlanId: PlanId; DimensionId: DimensionId; Quantity: Quantity; ResourceId: string }

type MarketplaceSubmissionError =
    | Duplicate of string
    | BadResourceId of string
    | InvalidEffectiveStartTime of string
    | CommunicationsProblem of string
    
type BatchUsageEventStatus =
    /// Accepted.
    | Accepted
    /// Expired usage.
    | Expired
    /// Duplicate usage provided.
    | Duplicate
    /// Error code.
    | ErrorCode
    /// The usage resource provided is invalid.
    | ResourceNotFound
    /// You are not authorized to provide usage for this resource.
    | ResourceNotAuthorized
    /// The resource is suspended or was never activated.
    | ResourceNotActive
    /// The dimension for which the usage is passed is invalid for this offer/plan.
    | InvalidDimension
    /// The quantity passed is lower or equal to 0.
    | InvalidQuantity
    /// The input is missing or malformed.
    | BadArgument

type MarketplaceSubmissionResponse =
    { 
    //{
    //    "status"
    //    "usageEventId/messageTime"
    //    "effectiveStartTime/planId/dimension/quantity/resourceId"
    //}
        Status: BatchUsageEventStatus
        UsageEventId: MeteringDateTime
        MessageTime: MeteringDateTime
        EffectiveStartTime: MeteringDateTime; PlanId: PlanId; DimensionId: DimensionId; Quantity: decimal; ResourceId: InternalResourceId
    }

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
        | Result.Error e -> 
            match e with
            | MarketplaceSubmissionError.Duplicate _ -> $"Duplicate: {x.Payload.EffectiveStartTime} {x.Payload.ResourceId}"
            | BadResourceId _ -> $"Bad resource ID: {x.Payload.ResourceId}" 
            | InvalidEffectiveStartTime _ -> $"InvalidEffectiveStartTime: {x.Payload.EffectiveStartTime} {x.Payload.ResourceId}"
            | CommunicationsProblem msg -> "Something bad happened: {msg}"
             