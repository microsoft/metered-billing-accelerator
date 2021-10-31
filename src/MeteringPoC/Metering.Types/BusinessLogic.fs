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
           | ConsumedQuantity consumedQuantity -> 
                consumedQuantity
                |> ConsumedQuantity.increaseConsumption now quantity
                |> ConsumedQuantity
           | IncludedQuantity iq ->
                match (iq.Annually, iq.Monthly) with
                | (None, None) -> 
                    quantity
                    |> ConsumedQuantity.create now 
                    |> ConsumedQuantity
                | (None, Some remainingMonthly) -> 
                    // if there's only monthly stuff, deduct from the monthly side
                    if remainingMonthly > quantity
                    then 
                        iq
                        |> IncludedQuantity.decreaseMonthly now quantity
                        |> IncludedQuantity
                    else 
                        quantity - remainingMonthly
                        |> ConsumedQuantity.create now 
                        |> ConsumedQuantity
                | (Some remainingAnnually, None) -> 
                    // if there's only annual stuff, deduct from there
                    if remainingAnnually > quantity
                    then
                        iq
                        |> IncludedQuantity.decreaseAnnually now quantity 
                        |> IncludedQuantity
                    else 
                        quantity - remainingAnnually
                        |> ConsumedQuantity.create now 
                        |> ConsumedQuantity
                | (Some remainingAnnually, Some remainingMonthly) -> 
                    // if there's both annual and monthly credits, first take from monthly, them from annual
                    if remainingMonthly > quantity
                    then 
                        iq
                        |> IncludedQuantity.decreaseMonthly now quantity
                        |> IncludedQuantity
                    else 
                        if remainingAnnually > quantity - remainingMonthly
                        then
                            iq
                            |> IncludedQuantity.removeMonthly now
                            |> IncludedQuantity.decreaseAnnually now (quantity - remainingMonthly)
                            |> IncludedQuantity
                        else 
                            quantity - remainingAnnually - remainingMonthly
                            |> ConsumedQuantity.create now
                            |> ConsumedQuantity

    let topupMonthlyCredits (now: MeteringDateTime) (quantity: Quantity) (pri: RenewalInterval) (meterValue: MeterValue) : MeterValue =
        match meterValue with 
        | (ConsumedQuantity(_)) -> 
            match pri with
                | Monthly -> quantity |> IncludedQuantity.createMonthly now
                | Annually -> quantity |> IncludedQuantity.createAnnually now
        | (IncludedQuantity(m)) -> // If there are other credits, just update the asked one
            match pri with
                | Monthly -> m |> IncludedQuantity.setMonthly now quantity |> IncludedQuantity
                | Annually -> m |> IncludedQuantity.setAnnually now quantity |> IncludedQuantity

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

    let closePreviousMeteringPeriod (config: MeteringConfigurationProvider) (state: Meter) : Meter =
        let isConsumedQuantity = function
            | ConsumedQuantity _ -> true
            | _ -> false

        // From the state.CurrentMeterValues, remove all those which are about consumption, and turn that into an API call
        let (consumedValues, includedValuesWhichDontNeedToBeReported) = 
            state.CurrentMeterValues
            |> Map.partition (fun _ -> isConsumedQuantity)

        let usagesToBeReported = 
            consumedValues
            |> Map.map (fun dimensionId cq -> 
                match cq with 
                | IncludedQuantity _ -> failwith "cannot happen"
                | ConsumedQuantity q -> 
                    { ResourceId = "ne" |> ResourceID.createFromSaaSSubscriptionID
                      Quantity = q.Amount |> Quantity.valueAsFloat
                      PlanDimension = { PlanId = state.Subscription.Plan.PlanId
                                        DimensionId = dimensionId }
                      EffectiveStartTime = state.LastProcessedMessage.PartitionTimestamp |> MeteringDateTime.beginOfTheHour } )
            |> Map.values
            |> List.ofSeq

        state
        |> Meter.setCurrentMeterValues includedValuesWhichDontNeedToBeReported
        |> Meter.addUsagesToBeReported usagesToBeReported

    let applyUsageEvent (config: MeteringConfigurationProvider) ((event: InternalUsageEvent), (currentPosition: MessagePosition)) (state : Meter) : Meter =
        let updateConsumption (currentMeterValues: CurrentMeterValues) : CurrentMeterValues = 
            let dimension : DimensionId = 
                state.InternalMetersMapping |> Map.find event.MeterName
                
            if currentMeterValues |> Map.containsKey dimension
            then 
                currentMeterValues
                |> Map.change dimension (applyConsumption event currentPosition)
            else
                let newConsumption = ConsumedQuantity (ConsumedQuantity.create event.Timestamp event.Quantity)
                currentMeterValues
                |> Map.add dimension newConsumption
        
        let closePreviousIntervalIfNeeded : (Meter -> Meter) = 
            let last = state.LastProcessedMessage.PartitionTimestamp
            let curr = currentPosition.PartitionTimestamp
            match previousBillingIntervalCanBeClosedNewEvent last curr with
            | Close -> closePreviousMeteringPeriod config
            | KeepOpen -> id

        state
        |> closePreviousIntervalIfNeeded
        |> Meter.applyToCurrentMeterValue updateConsumption
        |> Meter.setLastProcessedMessage currentPosition // Todo: Decide where to update the position

    let handleAggregatorBooted (config: MeteringConfigurationProvider) (state: Meter) : Meter =
        match previousBillingIntervalCanBeClosedWakeup config state.LastProcessedMessage.PartitionTimestamp with
        | Close -> state |> closePreviousMeteringPeriod config
        | KeepOpen -> state
        
    let handleUnsuccessfulMeterSubmission (config: MeteringConfigurationProvider) (failedSubmission: MeteringAPIUsageEventDefinition) (state: Meter) : Meter =
        // todo logging here, alternatively report in a later period?
        state
        
    let handleUsageSubmissionToAPI (config: MeteringConfigurationProvider) (usageSubmissionResult: UsageSubmittedToAPIResult) (state: Meter) : Meter =
        match usageSubmissionResult with
        | Ok successfulSubmission -> state |> Meter.removeUsageToBeReported successfulSubmission
        | Error (ex, failedSubmission) -> state |> handleUnsuccessfulMeterSubmission config failedSubmission 

    let computeIncludedQuantity (now: MeteringDateTime) (x: BillingDimension seq) : CurrentMeterValues = 
        x
        |> Seq.map(fun bd -> (bd.DimensionId, IncludedQuantity { Monthly = bd.IncludedQuantity.Monthly; Annually = bd.IncludedQuantity.Annually; Created = now; LastUpdate = now }))
        |> Map.ofSeq
        
    let topupMonthlyCreditsOnNewSubscription (time: MeteringDateTime) (state: Meter) : Meter =
        state
        |> Meter.setCurrentMeterValues (state.Subscription.Plan.BillingDimensions |> computeIncludedQuantity time)

    let createNewSubscription (subscriptionCreationInformation: SubscriptionCreationInformation) (messagePosition: MessagePosition) : Meter =
        // When we receive the creation of a subscription
        { Subscription = subscriptionCreationInformation.Subscription
          InternalMetersMapping = subscriptionCreationInformation.InternalMetersMapping
          LastProcessedMessage = messagePosition
          CurrentMeterValues = Map.empty
          UsageToBeReported = List.empty }
        |> topupMonthlyCreditsOnNewSubscription messagePosition.PartitionTimestamp

    let handleEvent (config: MeteringConfigurationProvider) (state: Meter option) { MeteringUpdateEvent = updateEvent; MessagePosition = position} : Meter option =        
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
            |> Meter.setLastProcessedMessage position
            |> Some
    
    let handleEvents (config: MeteringConfigurationProvider) (state: Meter option) (events: MeteringEvent list) : Meter option =
        events |> List.fold (handleEvent config) state

    let getState : Meter option =
        None

    let test =
        let config =
            { CurrentTimeProvider = "2021-10-27--21-35-00" |> MeteringDateTime.fromStr |> CurrentTimeProvider.AlwaysReturnSameTime
              SubmitMeteringAPIUsageEvent = SubmitMeteringAPIUsageEvent.Discard
              GracePeriod = Duration.FromHours(6.0) }
        let inputs : (MeteringEvent list) = []
        let state = Meter.initial
        let result = inputs |> handleEvents config state
        result
