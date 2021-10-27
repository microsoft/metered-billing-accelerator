namespace Metering

open NodaTime
open Metering.Types
open Metering.Types.EventHub

module RenewalInterval =
    let duration (pre: RenewalInterval) =
        match pre with
        | Monthly -> Period.FromMonths(1)
        | Annually -> Period.FromYears(1)

    let add (pre: RenewalInterval) (i: uint) : Period =
        match pre with
        | Monthly -> Period.FromMonths(int i)
        | Annually -> Period.FromYears(int i)

module Subscription =
    let create plan pri subscriptionStart = 
        { RenewalInterval = pri ; SubscriptionStart = subscriptionStart ; Plan = plan }

module BillingPeriod =
    /// Compute the n'th BillingPeriod for a given subscription.
    let createFromIndex (subscription : Subscription) (n: uint) : BillingPeriod =
        let period : (uint -> Period) = RenewalInterval.add subscription.RenewalInterval
        let add (period: Period) (x: MeteringDateTime) : MeteringDateTime =
            let r = x.LocalDateTime + period
            MeteringDateTime(r, DateTimeZone.Utc, Offset.Zero)

        { Start = subscription.SubscriptionStart |> add (period (n))
          End = subscription.SubscriptionStart |> add (period (n+1u) - Period.FromDays(1) - Period.FromSeconds(1L))
          Index = n }

    /// Determine in which BillingPeriod the given dateTime is.
    let determineBillingPeriod (sub: Subscription) (dateTime: MeteringDateTime) : Result<BillingPeriod, BusinessError> =
        if dateTime.LocalDateTime < sub.SubscriptionStart.LocalDateTime
        then DayBeforeSubscription |> Error 
        else 
            let diff = dateTime.LocalDateTime - sub.SubscriptionStart.LocalDateTime

            match sub.RenewalInterval with
                | Monthly -> diff.Years * 12 + diff.Months
                | Annually -> diff.Years
            |> uint |> createFromIndex sub |> Ok
    
    /// Whether the given dateTime is in the given BillingPeriod
    let isInBillingPeriod ({ Start = s; End = e }: BillingPeriod) (dateTime: MeteringDateTime) : bool =
        s.LocalDateTime <= dateTime.LocalDateTime && dateTime.LocalDateTime <= e.LocalDateTime

    type BillingPeriodResult =
        /// The given date is before the customer subscribed to the offer.
        | DateBeforeSubscription 
        // The date belongs to a previous BillingPeriod.
        | DateBelongsToPreviousBillingPeriod
        // The date belongs to the current BillingPeriod.
        | SameBillingPeriod
        // The date is n BillingPeriods ago
        | BillingPeriodsAgo of uint

    // Determine 
    let getBillingPeriodDelta (sub: Subscription) (previous: MeteringDateTime) (current: MeteringDateTime) : BillingPeriodResult =
        let dbp = determineBillingPeriod sub 
        match (dbp previous, dbp current) with
            | (Error(DayBeforeSubscription), _) -> DateBeforeSubscription
            | (_, Error(DayBeforeSubscription)) -> DateBeforeSubscription
            | Ok({ Index = p}), Ok({Index = c}) -> 
                match (p, c) with
                    | (p, c) when p < c -> BillingPeriodsAgo (c - p)
                    | (p, c) when p = c -> SameBillingPeriod
                    | _ -> DateBelongsToPreviousBillingPeriod

module Logic =
    open MarketPlaceAPI

    /// Subtracts the given Quantity from a MeterValue 
    let subtractQuantityFromMeterValue (meterValue: MeterValue) (quantity: Quantity) : MeterValue =
        meterValue
        |> function
           | ConsumedQuantity({ Amount = consumed}) -> ConsumedQuantity({ Amount = consumed + quantity})
           | IncludedQuantity({ Annually = annually; Monthly = monthly }) ->
                match (annually, monthly) with
                | (None, None) -> ConsumedQuantity { Amount = quantity }
                | (None, Some remainingMonthly) -> 
                        // if there's only monthly stuff, deduct from the monthly side
                        if remainingMonthly > quantity
                        then IncludedQuantity { Annually = None; Monthly = Some (remainingMonthly - quantity) }
                        else ConsumedQuantity { Amount = quantity - remainingMonthly }
                | (Some remainingAnnually, None) -> 
                        // if there's only annual stuff, deduct from the monthly side
                        if remainingAnnually > quantity
                        then IncludedQuantity { Annually = Some (remainingAnnually - quantity); Monthly = None}
                        else ConsumedQuantity { Amount = quantity - remainingAnnually }
                | (Some remainingAnnually, Some remainingMonthly) -> 
                        // if there's both annual and monthly credits, first take from monthly, them from annual
                        if remainingMonthly > quantity
                        then IncludedQuantity { Annually =  Some remainingAnnually; Monthly = Some (remainingMonthly - quantity) }
                        else 
                            let deductFromAnnual = quantity - remainingMonthly
                            if remainingAnnually > deductFromAnnual
                            then IncludedQuantity { Annually = Some (remainingAnnually - deductFromAnnual); Monthly = None }
                            else ConsumedQuantity { Amount = deductFromAnnual - remainingAnnually }

    let topupMonthlyCredits (quantity: Quantity) (pri: RenewalInterval) (meterValue: MeterValue) : MeterValue =
        match meterValue with 
        | (ConsumedQuantity(_)) -> 
            match pri with
                | Monthly -> IncludedQuantity { Annually = None; Monthly = Some quantity }
                | Annually -> IncludedQuantity { Annually = Some quantity; Monthly = None } 
        | (IncludedQuantity(m)) -> // If there are other credits, just update the asked one
            match pri with
                | Monthly -> IncludedQuantity { m with Monthly = Some quantity }
                | Annually -> IncludedQuantity { m with Annually = Some quantity }

    let applyConsumption (event: InternalUsageEvent) (current: MeterValue option) : MeterValue option =
        Option.bind ((fun q m -> Some (q |> subtractQuantityFromMeterValue m )) event.Quantity) current

    type CloseBillingPeriod =
        | KeepOpen
        | Close

    let previousBillingIntervalCanBeClosedNewEvent (previous: MeteringDateTime) (eventTime: MeteringDateTime) : CloseBillingPeriod =
        if previous.Hour <> eventTime.Hour || eventTime - previous >= Duration.FromHours(1.0)
        then Close
        else KeepOpen

    let previousBillingIntervalCanBeClosedWakeup  (previous: MeteringDateTime) (currentTime: CurrentTimeProvider) (gracePeriod: Duration) : CloseBillingPeriod =
        if currentTime() - previous >= gracePeriod
        then Close
        else KeepOpen

    //let previousBillingIntervalCanBeClosed (previous: MeteringDateTime) (currentTime: CurrentTimeProvider) (currentEvent: MeteringDateTime option) (gracePeriod: Duration) : CloseBillingPeriod =
    //    let close =
    //        match currentEvent with
    //        // If we're being triggered without a currentEvent, then close the previous billing interval if more time than the grace period passed
    //        | None -> (currentTime() - previous) >= gracePeriod
    //        // If there's a concrete new event, close if the event belongs to a different hour than the last event
    //        | Some eventTime -> (previous.Hour <> eventTime.Hour) || ((eventTime - previous) >= Duration.FromHours(1.0)) 
    //    if close then Close else KeepOpen

    let applyUsageEvent ((event: InternalUsageEvent), (currentPosition: MessagePosition)) (state : MeteringState) : MeteringState =
        let lastPosition = state.LastProcessedMessage

        let dimension = 
            state.InternalMetersMapping |> Map.find event.MeterName

        let updateConsumption : (CurrentMeterValues -> CurrentMeterValues) = 
            Map.change dimension (applyConsumption event)
        
        state
        |> MeteringState.applyToCurrentMeterValue updateConsumption
        |> MeteringState.setLastProcessedMessage currentPosition // Todo: Decide where to update the position

    let handleAggregatorBooted (state : MeteringState) : MeteringState =
        failwith "not implemented"

    let handleUnsuccessfulMeterSubmission (failedSubmission: MeteringAPIUsageEventDefinition) (state: MeteringState) : MeteringState =
        // todo logging here, alternatively report in a later period?
        state
        
    let handleUsageSubmissionToAPI (usageSubmissionResult: UsageSubmittedToAPIResult) (state: MeteringState) : MeteringState =
        match usageSubmissionResult with
        | Ok successfulSubmission -> state |> MeteringState.removeUsageToBeReported successfulSubmission
        | Error (ex, failedSubmission) -> state |> handleUnsuccessfulMeterSubmission failedSubmission 

    let computeIncludedQuantity (x: BillingDimension seq) : CurrentMeterValues = 
        x
        |> Seq.map(fun bd -> (bd.DimensionId, IncludedQuantity bd.IncludedQuantity))
        |> Map.ofSeq
        
    let topupMonthlyCreditsOnNewSubscription (state: MeteringState) : MeteringState =
        state
        |> MeteringState.setCurrentMeterValues (state.Subscription.Plan.BillingDimensions |> computeIncludedQuantity)

    let createNewSubscription (subscriptionCreationInformation: SubscriptionCreationInformation) (messagePosition: MessagePosition) : MeteringState =
        // When we receive the creation of a subscription
        { Subscription = subscriptionCreationInformation.Subscription
          InternalMetersMapping = subscriptionCreationInformation.InternalMetersMapping
          LastProcessedMessage = messagePosition
          CurrentMeterValues = Map.empty
          UsageToBeReported = List.empty }
        |> topupMonthlyCreditsOnNewSubscription

    let handleEvent (state: MeteringState option) (meteringEvent: MeteringEvent) : MeteringState option =
        match state with 
        | None -> 
            match meteringEvent.MeteringUpdateEvent with 
                | (SubscriptionPurchased subInfo) -> createNewSubscription subInfo meteringEvent.MessagePosition |> Some
                | _ -> None // without a subscription, we ignore all othes events
        | Some state -> 
            match meteringEvent.MeteringUpdateEvent with             
                | (UsageReported usage) -> state |> applyUsageEvent (usage, meteringEvent.MessagePosition) 
                | (UsageSubmittedToAPI submission) -> state |> handleUsageSubmissionToAPI submission 
                | (AggregatorBooted) -> state |> handleAggregatorBooted
                | (SubscriptionPurchased _) -> state // Once it's configured, no way to update
            |> MeteringState.setLastProcessedMessage meteringEvent.MessagePosition
            |> Some
    
    let handleEvents (state : MeteringState option) (events : MeteringEvent list) : MeteringState option =
        events |> List.fold handleEvent state 

    let getState : MeteringState option =
        None

    let test =
        let inputs : (MeteringEvent list) = []
        let state = MeteringState.initial
        let result = inputs |> List.fold handleEvent state
        result
