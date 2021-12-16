namespace Metering.Types

open System

// https://docs.microsoft.com/en-us/azure/marketplace/marketplace-metering-service-apis
type MarketplaceRequest = // From aggregator to metering API
    { /// Time in UTC when the usage event occurred, from now and until 24 hours back.
      EffectiveStartTime: MeteringDateTime
      
      /// ID of the plan purchased for the offer.
      PlanId: PlanId
      
      /// Custom dimension identifier.
      DimensionId: DimensionId
      
      /// How many units were consumed for the date and hour specified in effectiveStartTime, must be greater than 0, can be integer or float value
      Quantity: Quantity

      /// Unique identifier of the resource against which usage is emitted, can be a resourceId or resourceUri
      ResourceId: InternalResourceId }

module MarketplaceRequest =
    let toStr (x: MarketplaceRequest) : string =
        $"Usage: {x.ResourceId |> InternalResourceId.toStr} {x.EffectiveStartTime} {x.PlanId}/{x.DimensionId}: {x.Quantity |> Quantity.toStr}"

type MarketplaceBatchRequest = 
    private Entries of MarketplaceRequest list

module MarketplaceBatchRequest =
    let values (Entries e) = e
    
    let createBatch (x: MarketplaceRequest seq) : MarketplaceBatchRequest = 
        x
        |> Seq.toList
        |> (fun l -> 
            if l.Length > 25
            then raise (new ArgumentException(message = $"A maximum of 25 {nameof(MarketplaceRequest)} item is allowed")) 
            else l)
        |> Entries
    
type SubmissionStatus =
    /// Accepted.
    | Accepted
    /// Expired usage.
    | Expired
    /// Duplicate usage provided.
    | Duplicate
    /// Error code.
    | UsageEventError
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

type UsageEventID = string

type ResourceURI = string

type MarketplaceSubmissionStatus =
    { /// The "status"
      Status: SubmissionStatus
      /// The "messageTime"
      MessageTime: MeteringDateTime
      /// The "usageEventId"
      UsageEventID: UsageEventID option
      /// The "resourceUri"
      ResourceURI: ResourceURI option }
    
type MarketplaceErrorCode =
    { Code: string 
      Message: string}

type MarketplaceSuccessResponse =
    { ///  "resourceId/effectiveStartTime/planId/dimension/quantity"
      RequestData: MarketplaceRequest
      /// "status"/"messageTime"/"usageEventId"/"resourceUri"
      Status: MarketplaceSubmissionStatus }

type MarketplaceErrorDuplicate =
    // Check that ./status=Duplicate ./error/code=Conflict ./error/additionalInfo/acceptedMessage/status=Duplicate
    { /// ./[resourceId/effectiveStartTime/planId/dimension/quantity]
      FailedRequest: MarketplaceRequest 
      /// ./[status/messageTime]
      FailureStatus: MarketplaceSubmissionStatus
      /// ./error/additionalInfo/acceptedMessage/...
      PreviouslyAcceptedMessage: MarketplaceSuccessResponse }

/// ./[code,message,target]
type MarketplaceArgumentErrorData =
    { Error: MarketplaceErrorCode
      Target: string }

module MarketplaceArgumentErrorData =
    let toStr (e: MarketplaceArgumentErrorData) : string = 
        $"{e.Error.Code}: {e.Error.Message}"

type MarketplaceGenericError =
    { RequestData: MarketplaceRequest
      Status: MarketplaceSubmissionStatus
      Error: MarketplaceArgumentErrorData
      ErrorDetails: MarketplaceArgumentErrorData list }

module MarketplaceGenericError =
    let toStr (e: MarketplaceGenericError) : string =
        $"{e.Error |> MarketplaceArgumentErrorData.toStr}"

    let resourceId (e: MarketplaceGenericError) =
        e.RequestData.ResourceId

type MarketplaceSubmissionError =
    | DuplicateSubmission of MarketplaceErrorDuplicate
    | ResourceNotFound of MarketplaceGenericError
    | Expired of MarketplaceGenericError
    | Generic of MarketplaceGenericError

type MarketplaceSubmissionResult = 
     Result<MarketplaceSuccessResponse, MarketplaceSubmissionError>

module MarketplaceSubmissionResult = 
    let resourceId (marketplaceSubmissionResult: MarketplaceSubmissionResult) : InternalResourceId =
        match marketplaceSubmissionResult with 
        | Ok s -> s.RequestData.ResourceId
        | Error e ->
            match e with 
            | DuplicateSubmission d -> d.FailedRequest.ResourceId
            | ResourceNotFound e -> e |> MarketplaceGenericError.resourceId
            | Expired e -> e |> MarketplaceGenericError.resourceId
            | Generic e -> e |> MarketplaceGenericError.resourceId

    let toStr (x: MarketplaceSubmissionResult) : string =
        sprintf "%A" x

type AzureHttpResponseHeaders =
    { /// The `x-ms-requestid` HTTP header.
      RequestID: string 
       
      /// The `x-ms-correlationid` HTTP header
      CorrelationID: string }

type MarketplaceBatchResponseDTO = 
    { Results: MarketplaceSubmissionResult list }
      
type MarketplaceResponse =
    { Headers: AzureHttpResponseHeaders 
      Result: MarketplaceSubmissionResult }

module MarketplaceResponse =
    let create (headers: AzureHttpResponseHeaders) (result: MarketplaceSubmissionResult) =
        { Headers = headers
          Result = result }

    let toStr (x: MarketplaceResponse) : string =
        match x.Result with
        | Ok x -> 
            let successfulRequest = x.RequestData
            $"{successfulRequest.EffectiveStartTime |> MeteringDateTime.toStr}: Usage submitted: {successfulRequest.ResourceId} {successfulRequest.PlanId |> PlanId.value}/{successfulRequest.DimensionId |> DimensionId.value}={successfulRequest.Quantity |> Quantity.valueAsFloat}"
        | Error e -> 
            match e with
            | DuplicateSubmission x -> $"Duplicate of {x.PreviouslyAcceptedMessage.RequestData.EffectiveStartTime} {x.PreviouslyAcceptedMessage.RequestData.ResourceId}"
            | ResourceNotFound x -> $"Bad resource ID: {x.RequestData.ResourceId}" 
            | Expired x -> $"InvalidEffectiveStartTime: {x.RequestData.EffectiveStartTime}"
            | Generic x -> $"Something bad happened: {x}"
              
type MarketplaceBatchResponse = 
    { Results: MarketplaceResponse list }

module MarketplaceBatchResponse = 
    let create (results: MarketplaceResponse list) = 
        { Results = results} 
