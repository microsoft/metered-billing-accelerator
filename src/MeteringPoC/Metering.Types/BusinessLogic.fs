namespace Metering

open NodaTime
open Metering.Types
open Metering.Types.EventHub

module RenewalInterval =
    let duration pre =
        match pre with
        | Monthly -> Period.FromMonths(1)
        | Annually -> Period.FromYears(1)

    let add pre (i: uint) =
        match pre with
        | Monthly -> Period.FromMonths(int i)
        | Annually -> Period.FromYears(int i)

module Subscription =
    let create planId pri subscriptionStart = 
        { RenewalInterval = pri ; SubscriptionStart = subscriptionStart ; PlanId = planId }

module BillingPeriod =
    open Metering.Types

    let localDateToStr (x: LocalDate) = x.ToString("yyyy-MM-dd", null)
    
    let toString { FirstDay = firstDay; LastDay = lastDay } =        
        sprintf "%s--%s" (localDateToStr firstDay) (localDateToStr lastDay)

    let createFromIndex (subscription : Subscription) (n: uint) : BillingPeriod =
        let periods : (uint -> Period) = RenewalInterval.add subscription.RenewalInterval
        { FirstDay = subscription.SubscriptionStart + (periods (n))
          LastDay = subscription.SubscriptionStart + (periods (n+1u)) - Period.FromDays(1)
          Index = n }

    let determineBillingPeriod (subscription : Subscription) (day: LocalDate) : Result<BillingPeriod, BusinessError> =
        if subscription.SubscriptionStart > day 
        then DayBeforeSubscription |> Result.Error
        else 
            let diff = day - subscription.SubscriptionStart
            let idx = 
                match subscription.RenewalInterval with
                    | Monthly -> diff.Years * 12 + diff.Months
                    | Annually -> diff.Years
                |> uint

            Ok(createFromIndex subscription idx)
    
    let isInBillingPeriod { FirstDay = firstDay; LastDay = lastDay } (day: LocalDate) : bool =
        firstDay <= day && day <= lastDay

    type BillingPeriodResult =
        | DateBeforeSubscription 
        | DateBelongsToPreviousBillingPeriod
        | SameBillingPeriod
        | BillingPeriodDistance of uint

    let getBillingPeriodDelta(subscription: Subscription) (previous: LocalDate) (current: LocalDate) : BillingPeriodResult =
        let determine = determineBillingPeriod subscription 
        match (determine previous, determine current) with
            | (Result.Error(DayBeforeSubscription), _) -> DateBeforeSubscription
            | (_, Result.Error(DayBeforeSubscription)) -> DateBeforeSubscription
            | Ok(p), Ok(c) -> 
                match (p, c) with
                    | (p, c) when p < c -> BillingPeriodDistance(c.Index - p.Index)
                    | (p, c) when p = c -> SameBillingPeriod
                    | _ -> DateBelongsToPreviousBillingPeriod

module Logic =
    let deduct (meterValue: MeterValue) (reported: Quantity) : MeterValue =
        meterValue
        |> function
           | ConsumedQuantity({ Amount = consumed}) -> ConsumedQuantity({ Amount = consumed + reported})
           | IncludedQuantity({ Annually = annually; Monthly = monthly }) ->
                match (annually, monthly) with
                | (None, None) -> ConsumedQuantity { Amount = reported }
                | (None, Some remainingMonthly) -> 
                        // if there's only monthly stuff, deduct from the monthly side
                        if remainingMonthly > reported
                        then IncludedQuantity { Annually = None; Monthly = Some (remainingMonthly - reported) }
                        else ConsumedQuantity { Amount = reported - remainingMonthly }
                | (Some remainingAnnually, None) -> 
                        // if there's only annual stuff, deduct from the monthly side
                        if remainingAnnually > reported
                        then IncludedQuantity { Annually = Some (remainingAnnually - reported); Monthly = None}
                        else ConsumedQuantity { Amount = reported - remainingAnnually }
                | (Some remainingAnnually, Some remainingMonthly) -> 
                        // if there's both annual and monthly credits, first take from monthly, them from annual
                        if remainingMonthly > reported
                        then IncludedQuantity { Annually =  Some remainingAnnually; Monthly = Some (remainingMonthly - reported) }
                        else 
                            let deductFromAnnual = reported - remainingMonthly
                            if remainingAnnually > deductFromAnnual
                            then IncludedQuantity { Annually = Some (remainingAnnually - deductFromAnnual); Monthly = None }
                            else ConsumedQuantity { Amount = deductFromAnnual - remainingAnnually }

    let topupMonthlyCredits (meterValue: MeterValue) (quantity: Quantity) (pri: RenewalInterval) : MeterValue =
        match meterValue with 
        | (ConsumedQuantity(_)) -> 
            match pri with
                | Monthly -> IncludedQuantity { Annually = None; Monthly = Some quantity }
                | Annually -> IncludedQuantity { Annually = Some quantity; Monthly = None } 
        | (IncludedQuantity(m)) -> // If there are other credits, just update the asked one
            match pri with
                | Monthly -> IncludedQuantity { m with Monthly = Some quantity }
                | Annually -> IncludedQuantity { m with Annually = Some quantity }

    let applyConsumption (event: InternalUsageEvent) (current: MeterValue option) =
        Option.bind ((fun q m -> Some (q |> deduct m )) event.Quantity) current

    let planDimensionFromInternalEvent (event: InternalUsageEvent) (meteringState : MeteringState) =
        meteringState.InternalMetersMapping
        |> Map.find event.MeterName

    let applyUsageEvent (event: InternalUsageEvent) (meteringState : MeteringState) =
        // TODO if no 

        let planDimension = 
            meteringState
            |> planDimensionFromInternalEvent event 

        let newCredits = 
            meteringState.CurrentMeterValues
            |> Map.change planDimension (applyConsumption event)
        
        { meteringState 
            with CurrentMeterValues = newCredits}

    let updatePosition (position: MessagePosition) (state: MeteringState) : MeteringState =
        { state with LastProcessedMessage = position}

    let handleSuccessfulMeterSubmission  (submission: MeteringAPIUsageEventDefinition) (state: MeteringState) : MeteringState =
        let listRemove v l =
            l |> List.filter (fun e -> not (e = v))
        { state with UsageToBeReported = state.UsageToBeReported |> listRemove submission }

    let handleUnsuccessfulMeterSubmission (failedSubmission: MeteringAPIUsageEventDefinition) (state: MeteringState) : MeteringState =
        // todo
        state
        
    let handleUsageSubmission (meter: UsageSubmissionResult) (state: MeteringState) : MeteringState =
        match meter with
        | Ok submission -> state |> handleSuccessfulMeterSubmission submission
        | Error (ex, failedSubmission) -> state |> handleUnsuccessfulMeterSubmission failedSubmission 

    open MarketPlaceAPI

    let selectedPlan (state: MeteringState) : Plan = 
        state.Plans
        |> List.find (fun i -> i.PlanId = state.InitialPurchase.PlanId)
        
    let topupMonthlyCreditsOnNewSubscription (state: MeteringState) : MeteringState =
        let plan = state |> selectedPlan
      
        // let topupMonthlyCredits (meterValue: MeterValue) (quantity: Quantity) (pri: RenewalInterval) : MeterValue =
        let rni = state.InitialPurchase.RenewalInterval

        let freshMeterValues : CurrentMeterValues =
            plan.BillingDimensions
            |> Seq.map(fun bd -> ({ DimensionId = bd.DimensionId; PlanId = plan.PlanId }, IncludedQuantity bd.IncludedQuantity))
            |> Map.ofSeq

        { state with CurrentMeterValues = freshMeterValues }

    let createNewSubscription (subscriptionCreationInformation: SubscriptionCreationInformation) (messagePosition: MessagePosition) : MeteringState =
        // When we receive the creation of a subscription
        { Plans = subscriptionCreationInformation.Plans
          InitialPurchase = subscriptionCreationInformation.InitialPurchase
          InternalMetersMapping = subscriptionCreationInformation.InternalMetersMapping
          LastProcessedMessage = messagePosition
          CurrentMeterValues = Map.empty
          UsageToBeReported = List.empty }
        |> topupMonthlyCreditsOnNewSubscription

    
    let handleEvent (state: MeteringState option) ({ MeteringUpdateEvent = update; MessagePosition = position }: MeteringEvent) : MeteringState option =
        match (state, update) with
        | (None, SubscriptionPurchased subInfo) ->
            createNewSubscription subInfo position
            |> Some
        | (Some state, UsageReported usage) -> 
            state
            |> applyUsageEvent usage
            |> updatePosition position
            |> Some
        | (Some state, UsageSubmittedToAPI submission) -> 
            state
            |> handleUsageSubmission submission
            |> updatePosition position
            |> Some
        | (Some state, SubscriptionPurchased _) -> Some state // Once it's configured, no way to update
        | (None, UsageReported _) -> None
        | (None, UsageSubmittedToAPI _) -> None
    
    let handleEvents (state : MeteringState option) (events : MeteringEvent list) : MeteringState option =
        events |> List.fold handleEvent state 

    let getState : MeteringState option =
        None

    let test =
        let inputs : (MeteringEvent list) = []
        let state : MeteringState option = getState
        let result = inputs |> List.fold handleEvent state
        result
