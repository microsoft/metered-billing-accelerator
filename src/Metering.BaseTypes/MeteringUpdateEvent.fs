// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open Metering.BaseTypes.EventHub

/// The events which are processed by the aggregator.
type MeteringUpdateEvent =
    /// Event to initialize the aggregator.
    | SubscriptionPurchased of SubscriptionCreationInformation

    | SubscriptionUpdated of SubscriptionUpdate

    | SubscriptionDeletion of MarketplaceResourceId

    /// Event representing usage / consumption. Send from the application to the aggregator.
    | UsageReported of InternalUsageEvent

    /// An aggregator-internal event to keep track of which events must be / have been submitted to the metering API.
    | UsageSubmittedToAPI of MarketplaceResponse

    /// The message payload could not be parsed into a processable entity.
    | UnprocessableMessage of UnprocessableMessage

    /// Clean up state
    | RemoveUnprocessedMessages of RemoveUnprocessedMessages

    | Ping of PingMessage

    member this.partitionKey
        with get() : string =
            match this with
            | SubscriptionPurchased x -> x.Subscription.MarketplaceResourceId.ToString()
            | SubscriptionUpdated x -> x.MarketplaceResourceId.ToString()
            | SubscriptionDeletion x -> x.ToString()
            | UsageReported x -> x.MarketplaceResourceId.ToString()
            | UsageSubmittedToAPI x -> x.Result |> MarketplaceSubmissionResult.partitionKey
            | UnprocessableMessage _ -> ""
            | RemoveUnprocessedMessages _ -> ""
            | Ping x -> x.PartitionID.value

    member this.MarketplaceResourceID
        with get() : MarketplaceResourceId option =
            match this with
            | SubscriptionPurchased x -> x.Subscription.MarketplaceResourceId |> Some
            | SubscriptionDeletion x -> x |> Some
            | UsageReported x -> x.MarketplaceResourceId |> Some
            | UsageSubmittedToAPI x ->
                match x.Result with
                | Ok x -> x.RequestData.MarketplaceResourceId |> Some
                | Error e ->
                    match e with
                    | DuplicateSubmission d -> d.FailedRequest.MarketplaceResourceId |> Some
                    | ResourceNotFound d -> d.RequestData.MarketplaceResourceId |> Some
                    | Expired d -> d.RequestData.MarketplaceResourceId |> Some
                    | Generic d -> d.RequestData.MarketplaceResourceId |> Some
            | UnprocessableMessage _ -> None
            | RemoveUnprocessedMessages _ -> None
            | Ping x -> None

    override this.ToString() =
        match this with
        | SubscriptionPurchased x -> x.ToString()
        | SubscriptionUpdated x -> x.ToString()
        | SubscriptionDeletion x -> $"Deletion of {x}"
        | UsageReported x -> x.ToString()
        | UsageSubmittedToAPI x -> x.Result |>  MarketplaceSubmissionResult.toStr
        | UnprocessableMessage p ->
            match p with
            | UnprocessableStringContent s -> $"Unprocessable payload: {s}"
            | UnprocessableByteContent b -> $"Unprocessable payload: {System.Convert.ToBase64String(b)}"
            | UnprocessableUsageEvent u -> $"Unprocessable usage payload: {u}"
        | RemoveUnprocessedMessages { PartitionID = p; Selection = x } ->
            match x with
            | BeforeIncluding x -> $"Removing messages older than sequence number {x + 1L} in partition {p}"
            | Exactly x  -> $"Removing messages with sequence number {x} in partition {p}"
            | All -> $"Removing all unprocessable messages in partition {p}"
        | Ping x -> $"Ping from {x.SendingHost}, sent {x.LocalTime} because of {x.PingReason}"

    member this.MessageType
        with get() : string =
            match this with
            | SubscriptionPurchased _ -> nameof(SubscriptionPurchased)
            | SubscriptionUpdated _ -> nameof(SubscriptionUpdated)
            | SubscriptionDeletion _ -> nameof(SubscriptionDeletion)
            | UsageReported _ -> nameof(UsageReported)
            | UsageSubmittedToAPI _ -> nameof(UsageSubmittedToAPI)
            | UnprocessableMessage _ -> nameof(UnprocessableMessage)
            | RemoveUnprocessedMessages _ -> nameof(RemoveUnprocessedMessages)
            | Ping _ -> nameof(PingMessage)
