module Metering.Billing

open NodaTime

type PlanRenewalInterval =
    | Monthly
    | Yearly

type BillingPeriod = { FirstDay: LocalDate ; LastDay: LocalDate ; Index: uint }

type Subscription = { SubscriptionStart: LocalDate ; PlanRenewalInterval: PlanRenewalInterval }

module PlanRenewalInterval =    
    let add pre (i: uint) =
        match pre with
        | Monthly -> Period.FromMonths(int i)
        | Yearly -> Period.FromYears(int i)

module BillingPeriod =
    let localDateToStr (x: LocalDate) = x.ToString("yyyy-MM-dd", null)
    
    let toString { FirstDay = firstDay; LastDay = lastDay } =        
        sprintf "%s--%s" (localDateToStr firstDay) (localDateToStr lastDay)

    let createFromIndex ({ SubscriptionStart = subscriptionStart ; PlanRenewalInterval = pri} : Subscription) (n: uint) : BillingPeriod =
        let periods : (uint -> Period) = PlanRenewalInterval.add pri
        { FirstDay = subscriptionStart + (periods (n))
          LastDay = subscriptionStart + (periods (n+1u)) - Period.FromDays(1)
          Index = n }

    let isInBillingPeriod { FirstDay = firstDay; LastDay = lastDay } (day: LocalDate) : bool =
        firstDay <= day && day <= lastDay

type BusinessError =
    | DayBeforeSubscription
    | NewDateFromPreviousBillingPeriod

type BillingPeriodComparison =
    | SameBillingPeriod
    | NewerBillingPeriod

module Subscription =
    let create pri subscriptionStart =
        { SubscriptionStart = subscriptionStart ; PlanRenewalInterval = pri}

    let determineBillingPeriod { SubscriptionStart = subscriptionStart ; PlanRenewalInterval = pri} (day: LocalDate) : Result<BillingPeriod, BusinessError> =
        if subscriptionStart > day 
        then Error(DayBeforeSubscription)
        else 
            let diff = day - subscriptionStart
            let idx = 
                match pri with
                    | Monthly -> diff.Years * 12 + diff.Months
                    | Yearly -> diff.Years
                |> uint

            BillingPeriod.createFromIndex { SubscriptionStart = subscriptionStart ; PlanRenewalInterval = pri} idx 
            |> Ok

    let isNewBillingPeriod (subscription: Subscription) (previous: LocalDate) (current: LocalDate) : Result<BillingPeriodComparison, BusinessError> =
        let check = determineBillingPeriod subscription
        match (check previous, check current) with
            | (Error(e), _) -> Error(e) 
            | (_, Error(e)) -> Error(e)
            | Ok(p), Ok(c) -> 
                match (p,c) with
                    | (p,c) when p < c -> Ok(NewerBillingPeriod)
                    | (p,c) when p = c -> Ok(SameBillingPeriod)
                    | _ -> Error(NewDateFromPreviousBillingPeriod)

