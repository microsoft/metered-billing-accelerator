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
    let subtractQuantityFromMeterValue (now: MeteringDateTime) (meterValue: MeterValue) (quantity: Quantity) : MeterValue =
        meterValue
        |> function
           | ConsumedQuantity q -> ConsumedQuantity { q with Amount = q.Amount + quantity ; LastUpdate = now }
           | IncludedQuantity q ->
                match (q.Annually, q.Monthly) with
                | (None, None) -> ConsumedQuantity { Amount = quantity; Created = now; LastUpdate = now }
                | (None, Some remainingMonthly) -> 
                        // if there's only monthly stuff, deduct from the monthly side
                        if remainingMonthly > quantity
                        then IncludedQuantity { q with Monthly = Some (remainingMonthly - quantity); LastUpdate = now }
                        else ConsumedQuantity { Amount = quantity - remainingMonthly; Created = now; LastUpdate = now }
                | (Some remainingAnnually, None) -> 
                        // if there's only annual stuff, deduct from the monthly side
                        if remainingAnnually > quantity
                        then IncludedQuantity { q with Annually = Some (remainingAnnually - quantity); LastUpdate = now}
                        else ConsumedQuantity { Amount = quantity - remainingAnnually; Created = now; LastUpdate = now }
                | (Some remainingAnnually, Some remainingMonthly) -> 
                        // if there's both annual and monthly credits, first take from monthly, them from annual
                        if remainingMonthly > quantity
                        then IncludedQuantity { q with Annually = Some remainingAnnually; Monthly = Some (remainingMonthly - quantity); LastUpdate = now }
                        else 
                            let deductFromAnnual = quantity - remainingMonthly
                            if remainingAnnually > deductFromAnnual
                            then IncludedQuantity { q with Annually = Some (remainingAnnually - deductFromAnnual); Monthly = None; LastUpdate = now }
                            else ConsumedQuantity { Amount = deductFromAnnual - remainingAnnually; Created = now; LastUpdate = now  }

    let topupMonthlyCredits (now: MeteringDateTime) (quantity: Quantity) (pri: RenewalInterval) (meterValue: MeterValue) : MeterValue =
        match meterValue with 
        | (ConsumedQuantity(_)) -> 
            match pri with
                | Monthly -> IncludedQuantity { Annually = None; Monthly = Some quantity; Created = now; LastUpdate = now }
                | Annually -> IncludedQuantity { Annually = Some quantity; Monthly = None; Created = now; LastUpdate = now  } 
        | (IncludedQuantity(m)) -> // If there are other credits, just update the asked one
            match pri with
                | Monthly -> IncludedQuantity { m with Monthly = Some quantity; LastUpdate = now }
                | Annually -> IncludedQuantity { m with Annually = Some quantity; LastUpdate = now }

    let applyConsumption (event: InternalUsageEvent) (currentPosition: MessagePosition) (current: MeterValue option) : MeterValue option =
        Option.bind ((fun q m -> Some (subtractQuantityFromMeterValue currentPosition.PartitionTimestamp m q)) event.Quantity) current

    type CloseBillingPeriod =
        | KeepOpen
        | Close

    let previousBillingIntervalCanBeClosedNewEvent (previous: MeteringDateTime) (eventTime: MeteringDateTime) : CloseBillingPeriod =
        if previous.Hour <> eventTime.Hour || eventTime - previous >= Duration.FromHours(1.0)
        then Close
        else KeepOpen

    let previousBillingIntervalCanBeClosedWakeup (config: MeteringConfigurationProvider) (previous: MeteringDateTime) : CloseBillingPeriod =
        if config.CurrentTimeProvider() - previous >= config.GracePeriod
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

    let applyUsageEvent (config: MeteringConfigurationProvider) ((event: InternalUsageEvent), (currentPosition: MessagePosition)) (state : MeteringState) : MeteringState =
        let lastPosition = state.LastProcessedMessage

        let dimension = 
            state.InternalMetersMapping |> Map.find event.MeterName

        let updateConsumption : (CurrentMeterValues -> CurrentMeterValues) = 
            Map.change dimension (applyConsumption event currentPosition)
        
        state
        |> MeteringState.applyToCurrentMeterValue updateConsumption
        |> MeteringState.setLastProcessedMessage currentPosition // Todo: Decide where to update the position

    let closePreviousMeteringPeriod (config: MeteringConfigurationProvider) (state: MeteringState) : MeteringState =
        let isConsumedQuantity = function
            | ConsumedQuantity _ -> true
            | _ -> false

        // From the state.CurrentMeterValues, remove all those which are about consumption, and turn that into an API call
        let (consumedValues, includedValuesWhichDontNeedToBeReported) = 
            state.CurrentMeterValues
            |> Map.partition (fun _ -> isConsumedQuantity)

        let getStartOfReportingRange (m: MeteringDateTime) : MeteringDateTime =
            let adjuster : System.Func<LocalDate, LocalDate> = 
                failwith ""
            MeteringDateTime(m.LocalDateTime.With(adjuster), m.Zone, m.Offset)

        let usagesToBeReported = 
            consumedValues
            |> Map.map (fun dimensionId cq -> 
                match cq with 
                | IncludedQuantity _ -> failwith "cannot happen"
                | ConsumedQuantity q -> 
                    { ResourceId = "" // TODO
                      Quantity = double q.Amount
                      PlanDimension = { PlanId = state.Subscription.Plan.PlanId
                                        DimensionId = dimensionId }
                      EffectiveStartTime = state.LastProcessedMessage.PartitionTimestamp |> getStartOfReportingRange } )
            |> Map.values
            |> List.ofSeq

        state
        |> MeteringState.setCurrentMeterValues includedValuesWhichDontNeedToBeReported
        |> MeteringState.addUsagesToBeReported usagesToBeReported

    let handleAggregatorBooted (config: MeteringConfigurationProvider) (state: MeteringState) : MeteringState =
        match previousBillingIntervalCanBeClosedWakeup config state.LastProcessedMessage.PartitionTimestamp with
        | Close -> state |> closePreviousMeteringPeriod config
        | KeepOpen -> state
        
    let handleUnsuccessfulMeterSubmission (config: MeteringConfigurationProvider) (failedSubmission: MeteringAPIUsageEventDefinition) (state: MeteringState) : MeteringState =
        // todo logging here, alternatively report in a later period?
        state
        
    let handleUsageSubmissionToAPI (config: MeteringConfigurationProvider) (usageSubmissionResult: UsageSubmittedToAPIResult) (state: MeteringState) : MeteringState =
        match usageSubmissionResult with
        | Ok successfulSubmission -> state |> MeteringState.removeUsageToBeReported successfulSubmission
        | Error (ex, failedSubmission) -> state |> handleUnsuccessfulMeterSubmission config failedSubmission 

    let computeIncludedQuantity (now: MeteringDateTime) (x: BillingDimension seq) : CurrentMeterValues = 
        x
        |> Seq.map(fun bd -> (bd.DimensionId, IncludedQuantity { Monthly = bd.IncludedQuantity.Monthly; Annually = bd.IncludedQuantity.Annually; Created = now; LastUpdate = now }))
        |> Map.ofSeq
        
    let topupMonthlyCreditsOnNewSubscription (time: MeteringDateTime) (state: MeteringState) : MeteringState =
        state
        |> MeteringState.setCurrentMeterValues (state.Subscription.Plan.BillingDimensions |> computeIncludedQuantity time)

    let createNewSubscription (subscriptionCreationInformation: SubscriptionCreationInformation) (messagePosition: MessagePosition) : MeteringState =
        // When we receive the creation of a subscription
        { Subscription = subscriptionCreationInformation.Subscription
          InternalMetersMapping = subscriptionCreationInformation.InternalMetersMapping
          LastProcessedMessage = messagePosition
          CurrentMeterValues = Map.empty
          UsageToBeReported = List.empty }
        |> topupMonthlyCreditsOnNewSubscription messagePosition.PartitionTimestamp

    let handleEvent (config: MeteringConfigurationProvider) (state: MeteringState option) { MeteringUpdateEvent = updateEvent; MessagePosition = position} : MeteringState option =        
        match state with 
        | None -> 
            match updateEvent with 
                | (SubscriptionPurchased subInfo) -> createNewSubscription subInfo position |> Some
                | _ -> None // without a subscription, we ignore all other events
        | Some state -> 
            match updateEvent with             
                | (UsageReported usage) -> state |> applyUsageEvent config (usage, position) 
                | (UsageSubmittedToAPI submission) -> state |> handleUsageSubmissionToAPI config submission 
                | (AggregatorBooted) -> state |> handleAggregatorBooted config
                | (SubscriptionPurchased _) -> state // Once it's configured, no way to update
            |> MeteringState.setLastProcessedMessage position
            |> Some
    
    let handleEvents (config: MeteringConfigurationProvider) (state: MeteringState option) (events: MeteringEvent list) : MeteringState option =
        events |> List.fold (handleEvent config) state

    let getState : MeteringState option =
        None

    let test =
        let config =
            { CurrentTimeProvider = "2021-10-27--21-35-00" |> MeteringDateTime.fromStr |> CurrentTimeProvider.AlwaysReturnSameTime
              SubmitMeteringAPIUsageEvent = SubmitMeteringAPIUsageEvent.Discard
              GracePeriod = Duration.FromHours(6.0) }
        let inputs : (MeteringEvent list) = []
        let state = MeteringState.initial
        let result = inputs |> handleEvents config state
        result
