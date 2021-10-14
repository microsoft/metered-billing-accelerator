module Metering.Billing

open NodaTime

type BusinessError =
    | DayBeforeSubscription
    | NewDateFromPreviousBillingPeriod     

type PlanRenewalInterval =
    | Monthly
    | Yearly

module PlanRenewalInterval =    
    let add pre (i: uint) =
        match pre with
        | Monthly -> Period.FromMonths(int i)
        | Yearly -> Period.FromYears(int i)

type Subscription =
    {
        PlanRenewalInterval: PlanRenewalInterval 
        SubscriptionStart: LocalDate
    }

module Subscription =
    let create pri subscriptionStart = 
        { PlanRenewalInterval = pri ; SubscriptionStart = subscriptionStart }

type BillingPeriod =
    { 
        FirstDay: LocalDate
        LastDay: LocalDate
        Index: uint
    }

module BillingPeriod =
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
        then Error(DayBeforeSubscription)
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
            | (Error(e), _) -> Error(e) 
            | (_, Error(e)) -> Error(e)
            | Ok(p), Ok(c) -> 
                match (p,c) with
                    | (p,c) when p <= c -> Ok(c.Index - p.Index)
                    | _ -> Error(NewDateFromPreviousBillingPeriod)

