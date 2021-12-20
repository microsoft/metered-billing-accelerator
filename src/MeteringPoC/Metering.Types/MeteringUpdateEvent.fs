namespace Metering.Types

/// The events which are processed by the aggregator.
type MeteringUpdateEvent =
    /// Event to initialize the aggregator.
    | SubscriptionPurchased of SubscriptionCreationInformation 

    /// Event representing usage / consumption. Send from the application to the aggregator.
    | UsageReported of InternalUsageEvent
    
    /// An aggregator-internal event to keep track of which events must be / have been submitted to the metering API.
    | UsageSubmittedToAPI of MarketplaceResponse

    /// The message payload could not be parsed into a processable entity.
    | UnprocessableMessage of UnprocessableMessage

    /// Clean up state
    | RemoveUnprocessedMessages of RemoveUnprocessedMessages

type LocalControlEvent =
    /// A heart beat signal to flush potential billing periods
    | AggregatorBooted

module MeteringUpdateEvent =
    let partitionKey (mue: MeteringUpdateEvent) : string =
        match mue with
        | SubscriptionPurchased x -> x.Subscription.InternalResourceId |> InternalResourceId.toStr
        | UsageReported x -> x.InternalResourceId |> InternalResourceId.toStr
        | UsageSubmittedToAPI x -> x.Result |> MarketplaceSubmissionResult.resourceId |> InternalResourceId.toStr
        | UnprocessableMessage _ -> ""
        | RemoveUnprocessedMessages _ -> ""

    let type_name (mue: MeteringUpdateEvent) : string =
        match mue with
        | SubscriptionPurchased _ -> nameof(SubscriptionPurchased)
        | UsageReported _ -> nameof(UsageReported)
        | UsageSubmittedToAPI _ -> nameof(UsageSubmittedToAPI)
        | UnprocessableMessage _ -> nameof(UnprocessableMessage)
        | RemoveUnprocessedMessages _ -> nameof(RemoveUnprocessedMessages)

    let toStr (mue: MeteringUpdateEvent) : string =
        match mue with
        | SubscriptionPurchased x -> x |> SubscriptionCreationInformation.toStr
        | UsageReported x -> x |> InternalUsageEvent.toStr
        | UsageSubmittedToAPI x -> x.Result |>  MarketplaceSubmissionResult.toStr        
        | UnprocessableMessage p -> 
            match p with
            | UnprocessableStringContent s -> $"Unprocessable payload: {s}"
            | UnprocessableByteContent b -> $"Unprocessable payload: {System.Convert.ToBase64String(b)}"
        | RemoveUnprocessedMessages { PartitionID = p; Selection = x } ->
            match x with 
            | BeforeIncluding x -> $"Removing messages older than sequence number {x + 1L} in partition {p}"
            | Exactly x  -> $"Removing messages with sequence number {x} in partition {p}"
