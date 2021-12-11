namespace Metering.Types

type UnprocessableMessage =
    | UnprocessableStringContent of string
    | UnprocessableByteContent of byte array

/// The events which are processed by the aggregator.
type MeteringUpdateEvent =
    /// Event to initialize the aggregator.
    | SubscriptionPurchased of SubscriptionCreationInformation 

    /// Event representing usage / consumption. Send from the application to the aggregator.
    | UsageReported of InternalUsageEvent
    
    /// An aggregator-internal event to keep track of which events must be / have been submitted to the metering API.
    | UsageSubmittedToAPI of MarketplaceSubmissionResult

    /// The message payload could not be parsed into a processable entity.
    | UnprocessableMessage of UnprocessableMessage

    /// A heart beat signal to flush potential billing periods
    | AggregatorBooted

module MeteringUpdateEvent =
    let partitionKey (mue: MeteringUpdateEvent) : string =
        match mue with
        | SubscriptionPurchased x -> x.Subscription.InternalResourceId |> InternalResourceId.toStr
        | UsageReported x -> x.InternalResourceId |> InternalResourceId.toStr
        | UsageSubmittedToAPI x -> x.Payload.ResourceId  |> InternalResourceId.toStr
        | AggregatorBooted -> null
        | UnprocessableMessage p -> null

    let toStr (mue: MeteringUpdateEvent) : string =
        match mue with
        | SubscriptionPurchased x -> x |> SubscriptionCreationInformation.toStr
        | UsageReported x -> x |> InternalUsageEvent.toStr
        | UsageSubmittedToAPI x -> x |> MarketplaceSubmissionResult.toStr
        | AggregatorBooted -> nameof(AggregatorBooted)
        | UnprocessableMessage p -> 
            match p with
            | UnprocessableStringContent s -> $"Unprocessable payload: {s}"
            | UnprocessableByteContent b -> $"Unprocessable payload: {System.Convert.ToBase64String(b)}"