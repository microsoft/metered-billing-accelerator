// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open System

// https://docs.microsoft.com/en-us/azure/marketplace/marketplace-metering-service-apis
/// From aggregator to metering API
[<CustomEquality; NoComparison>]
type MarketplaceRequest =
    { /// Time in UTC when the usage event occurred, from now and until 24 hours back.
      EffectiveStartTime: MeteringDateTime

      /// ID of the plan purchased for the offer.
      PlanId: PlanId

      /// Custom dimension identifier.
      DimensionId: DimensionId

      /// How many units were consumed for the date and hour specified in effectiveStartTime, must be greater than 0, can be integer or float value
      Quantity: Quantity

      /// Unique identifier of the resource against which usage is emitted, can be a resourceId, or resourceUri, or both
      MarketplaceResourceId: MarketplaceResourceId }

    override this.ToString() =
        $"Usage: {this.MarketplaceResourceId.ToString()} {this.EffectiveStartTime} {this.PlanId}/{this.DimensionId}: {this.Quantity.ToString()}"

    override this.Equals other =
        let equal =
            match other with
            | :? MarketplaceRequest as x ->
                (x.EffectiveStartTime = this.EffectiveStartTime) &&
                (x.PlanId = this.PlanId) &&
                (x.DimensionId = this.DimensionId) &&
                (x.Quantity = this.Quantity) &&
                (x.MarketplaceResourceId.Matches this.MarketplaceResourceId)
            | _ -> false
        equal

     override this.GetHashCode () =
        (this.EffectiveStartTime.GetHashCode()) ^^^
        (this.PlanId.GetHashCode()) ^^^
        (this.DimensionId.GetHashCode()) ^^^
        (this.Quantity.GetHashCode())

type MarketplaceBatchRequest =
    private | Value of MarketplaceRequest list

    member this.values
        with get() =
            let v (Value x) = x
            this |> v

    static member create x = (Value x)

    static member createBatch (x: MarketplaceRequest seq) =
        x
        |> Seq.toList
        |> (fun l ->
            if l.Length > 25
            then raise (new ArgumentException(message = $"A maximum of 25 {nameof(MarketplaceRequest)} item is allowed"))
            else l)
        |> MarketplaceBatchRequest.create

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

type MarketplaceSubmissionStatus =
    { /// The "status"
      Status: SubmissionStatus
      /// The "messageTime"
      MessageTime: MeteringDateTime
      /// The "usageEventId"
      UsageEventID: UsageEventID option }

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

     override this.ToString() = $"{this.Error.Code}: {this.Error.Message}"

type MarketplaceGenericError =
    { RequestData: MarketplaceRequest
      Status: MarketplaceSubmissionStatus
      Error: MarketplaceArgumentErrorData
      ErrorDetails: MarketplaceArgumentErrorData list }

    override this.ToString() = this.Error.ToString()

    member this.resourceId () = this.RequestData.MarketplaceResourceId

type MarketplaceSubmissionError =
    | DuplicateSubmission of MarketplaceErrorDuplicate
    | ResourceNotFound of MarketplaceGenericError
    | Expired of MarketplaceGenericError
    | Generic of MarketplaceGenericError

type MarketplaceSubmissionResult =
     Result<MarketplaceSuccessResponse, MarketplaceSubmissionError>

module MarketplaceSubmissionResult =
    let marketplaceResourceId (marketplaceSubmissionResult: MarketplaceSubmissionResult) : MarketplaceResourceId =
        match marketplaceSubmissionResult with
        | Ok s -> s.RequestData.MarketplaceResourceId
        | Error e ->
            match e with
            | DuplicateSubmission d -> d.FailedRequest.MarketplaceResourceId
            | ResourceNotFound e -> e.resourceId()
            | Expired e -> e.resourceId()
            | Generic e -> e.resourceId()

    let partitionKey (marketplaceSubmissionResult: MarketplaceSubmissionResult) : string =
        marketplaceSubmissionResult
        |> marketplaceResourceId
        |> (fun x -> x.PartitionKey)

    let toStr (x: MarketplaceSubmissionResult) : string =
        match x with
        | Ok success -> $"Marketplace OK: {success.RequestData.MarketplaceResourceId.ToString()}/{success.RequestData.PlanId.value}/{success.RequestData.DimensionId.value} Period {success.RequestData.EffectiveStartTime |> MeteringDateTime.toStr} {success.Status.MessageTime |> MeteringDateTime.toStr} {success.RequestData.Quantity.ToString()}"
        | Error e ->
            match e with
            | DuplicateSubmission d -> $"Marketplace Duplicate: {d.PreviouslyAcceptedMessage.RequestData.MarketplaceResourceId.ToString()} {d.PreviouslyAcceptedMessage.RequestData.EffectiveStartTime |> MeteringDateTime.toStr}"
            | ResourceNotFound d -> $"Marketplace NotFound: ResourceId {d.RequestData.MarketplaceResourceId.ToString()}"
            | Expired d ->
                let tooLate = (d.Status.MessageTime - d.RequestData.EffectiveStartTime)
                $"Marketplace Expired: {d.RequestData.MarketplaceResourceId.ToString()}: Time delta between billingTime and submission: {tooLate}"
            | Generic d ->  sprintf "Marketplace Generic Error: %A" d

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

    override this.ToString() =
        match this.Result with
        | Ok x ->
            let successfulRequest = x.RequestData
            $"{successfulRequest.EffectiveStartTime |> MeteringDateTime.toStr}: Usage submitted: {successfulRequest.MarketplaceResourceId} {successfulRequest.PlanId.value}/{successfulRequest.DimensionId.value}={successfulRequest.Quantity.AsFloat}"
        | Error e ->
            match e with
            | DuplicateSubmission x -> $"Duplicate of {x.PreviouslyAcceptedMessage.RequestData.EffectiveStartTime} {x.PreviouslyAcceptedMessage.RequestData.MarketplaceResourceId}"
            | ResourceNotFound x -> $"Bad resource ID: {x.RequestData.MarketplaceResourceId}"
            | Expired x -> $"InvalidEffectiveStartTime: {x.RequestData.EffectiveStartTime}"
            | Generic x -> $"Something bad happened: {x}"

    static member create (headers: AzureHttpResponseHeaders) (result: MarketplaceSubmissionResult) =
        { Headers = headers
          Result = result }

type MarketplaceBatchResponse =
    { Results: MarketplaceResponse list }

    static member create (results: MarketplaceResponse list) =
        { Results = results}
