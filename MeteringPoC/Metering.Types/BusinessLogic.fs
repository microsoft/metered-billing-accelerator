namespace Metering

open NodaTime
open Metering.Types

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

module MeterValue =
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

module BusinessLogic =
    let applyConsumption (event: InternalUsageEvent) (current: MeterValue option) : MeterValue option =
        Option.bind ((fun r m -> Some (r |> MeterValue.deduct m )) event.Quantity) current

    let applyUsageEvent (current: CurrentBillingState) (event: InternalUsageEvent) : CurrentBillingState =
        let newCredits = 
            current.CurrentMeterValues
            |> Map.change event.MeterName (applyConsumption event)
        
        { current 
            with CurrentMeterValues = newCredits}

    let applyUsageEvents (state: CurrentBillingState) (usageEvents: InternalUsageEvent list) : CurrentBillingState =
        usageEvents |> List.fold applyUsageEvent state

