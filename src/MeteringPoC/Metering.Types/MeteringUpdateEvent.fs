namespace Metering.Types

/// The events which are processed by the aggregator.
type MeteringUpdateEvent =
    /// Event to initialize the aggregator.
    | SubscriptionPurchased of SubscriptionCreationInformation 

    /// Event representing usage / consumption. Send from the application to the aggregator.
    | UsageReported of InternalUsageEvent
    
    /// An aggregator-internal event to keep track of which events must be / have been submitted to the metering API.
    | UsageSubmittedToAPI of MarketplaceSubmissionResult

    /// A heart beat signal to flush potential billing periods
    | AggregatorBooted

module MeteringUpdateEvent =
    let partitionKey (mue: MeteringUpdateEvent) : string =
        match mue with
        | SubscriptionPurchased x -> x.Subscription.InternalResourceId |> InternalResourceId.toStr
        | UsageReported x -> x.InternalResourceId |> InternalResourceId.toStr
        | UsageSubmittedToAPI x -> x.Payload.ResourceId  |> InternalResourceId.toStr
        | AggregatorBooted -> null

    let toStr (mue: MeteringUpdateEvent) : string =
        match mue with
        | SubscriptionPurchased x -> x |> SubscriptionCreationInformation.toStr
        | UsageReported x -> x |> InternalUsageEvent.toStr
        | UsageSubmittedToAPI x -> x |> MarketplaceSubmissionResult.toStr
        | AggregatorBooted -> nameof(AggregatorBooted)
        