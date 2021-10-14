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
    let create pri subscriptionStart = 
        { PlanRenewalInterval = pri ; SubscriptionStart = subscriptionStart }

module BillingPeriod =
    open Metering.Types

    let localDateToStr (x: LocalDate) = x.ToString("yyyy-MM-dd", null)
    
    let toString { FirstDay = firstDay; LastDay = lastDay } =        
        sprintf "%s--%s" (localDateToStr firstDay) (localDateToStr lastDay)

    let createFromIndex ({ SubscriptionStart = subscriptionStart ; PlanRenewalInterval = pri} : Subscription) (n: uint) : BillingPeriod =
        let periods : (uint -> Period) = PlanRenewalInterval.add pri
        { FirstDay = subscriptionStart + (periods (n))
          LastDay = subscriptionStart + (periods (n+1u)) - Period.FromDays(1)
          Index = n }

    let determineBillingPeriod { SubscriptionStart = subscriptionStart ; PlanRenewalInterval = pri} (day: LocalDate) : Result<BillingPeriod, BusinessError> =
        if subscriptionStart > day 
        then DayBeforeSubscription |> Result.Error
        else 
            let diff = day - subscriptionStart
            let idx = 
                match pri with
                    | Monthly -> diff.Years * 12 + diff.Months
                    | Yearly -> diff.Years
                |> uint

            createFromIndex { SubscriptionStart = subscriptionStart ; PlanRenewalInterval = pri} idx 
            |> Ok
    
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
    let deduct ({ Quantity = reported}: InternalUsageEvent) (state: CurrentConsumptionBillingPeriod) : CurrentConsumptionBillingPeriod option =
        state
        |> function
            | IncludedQuantity ({ Quantity = remaining}) -> 
                if remaining > reported
                then IncludedQuantity ({ Quantity = remaining - reported})
                else ConsumedQuantity({ Quantity = reported - remaining})
            | ConsumedQuantity(consumed) ->
                ConsumedQuantity({ Quantity = consumed.Quantity + reported })
        |> Some

    let applyConsumption (event: InternalUsageEvent) (current: CurrentConsumptionBillingPeriod option) : CurrentConsumptionBillingPeriod option =
        Option.bind (deduct event) current

    let applyUsageEvent (current: CurrentBillingState) (event: InternalUsageEvent) : CurrentBillingState =
        let newCredits = 
            current.CurrentCredits
            |> Map.change event.MeterName (applyConsumption event)
        
        { current 
            with CurrentCredits = newCredits}

    let applyUsageEvents (state: CurrentBillingState) (usageEvents: InternalUsageEvent list) : CurrentBillingState =
        usageEvents |> List.fold applyUsageEvent state

