namespace Metering

open NodaTime
open Metering.Types

module PlanRenewalInterval =
    let duration pre =
        match pre with
        | Monthly -> Period.FromMonths(1)
        | Yearly -> Period.FromYears(1)

    let add pre (i: uint) =
        match pre with
        | Monthly -> Period.FromMonths(int i)
        | Yearly -> Period.FromYears(int i)

module Subscription =
    let create planId pri subscriptionStart = 
        { PlanRenewalInterval = pri ; SubscriptionStart = subscriptionStart ; PlanId = planId }

module BillingPeriod =
    open Metering.Types

    let localDateToStr (x: LocalDate) = x.ToString("yyyy-MM-dd", null)
    
    let toString { FirstDay = firstDay; LastDay = lastDay } =        
        sprintf "%s--%s" (localDateToStr firstDay) (localDateToStr lastDay)

    let createFromIndex (subscription : Subscription) (n: uint) : BillingPeriod =
        let periods : (uint -> Period) = PlanRenewalInterval.add subscription.PlanRenewalInterval
        { FirstDay = subscription.SubscriptionStart + (periods (n))
          LastDay = subscription.SubscriptionStart + (periods (n+1u)) - Period.FromDays(1)
          Index = n }

    let determineBillingPeriod (subscription : Subscription) (day: LocalDate) : Result<BillingPeriod, BusinessError> =
        if subscription.SubscriptionStart > day 
        then DayBeforeSubscription |> Result.Error
        else 
            let diff = day - subscription.SubscriptionStart
            let idx = 
                match subscription.PlanRenewalInterval with
                    | Monthly -> diff.Years * 12 + diff.Months
                    | Yearly -> diff.Years
                |> uint

            Ok(createFromIndex subscription idx)
    
    let isInBillingPeriod { FirstDay = firstDay; LastDay = lastDay } (day: LocalDate) : bool =
        firstDay <= day && day <= lastDay

    let getBillingPeriodDelta(subscription: Subscription) (previous: LocalDate) (current: LocalDate) : Result<uint, BusinessError> =
        let check = determineBillingPeriod subscription
        match (check previous, check current) with
            | (Result.Error(e), _) -> Result.Error(e) 
            | (_, Result.Error(e)) -> Result.Error(e)
            | Ok(p), Ok(c) -> 
                match (p,c) with
                    | (p,c) when p <= c -> Ok(c.Index - p.Index)
                    | _ -> Result.Error(NewDateFromPreviousBillingPeriod)

module BusinessLogic =
    let deduct ({ Quantity = reported}: InternalUsageEvent) (state: MeterValue) : MeterValue option =
        state
        |> function
            | IncludedQuantity ({ Quantity = remaining}) -> 
                if remaining > reported
                then IncludedQuantity ({ Quantity = remaining - reported})
                else ConsumedQuantity({ Quantity = reported - remaining})
            | ConsumedQuantity(consumed) ->
                ConsumedQuantity({ Quantity = consumed.Quantity + reported })
        |> Some

    let applyConsumption (event: InternalUsageEvent) (current: MeterValue option) : MeterValue option =
        Option.bind (deduct event) current

    let applyUsageEvent (current: CurrentBillingState) (event: InternalUsageEvent) : CurrentBillingState =
        let newCredits = 
            current.CurrentMeterValues
            |> Map.change event.MeterName (applyConsumption event)
        
        { current 
            with CurrentMeterValues = newCredits}

    let applyUsageEvents (state: CurrentBillingState) (usageEvents: InternalUsageEvent list) : CurrentBillingState =
        usageEvents |> List.fold applyUsageEvent state

