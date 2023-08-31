// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

type Subscription =
    { /// The details of the plan
      Plan: Plan

      /// The SaaS subscription ID or managed app ID.
      MarketplaceResourceId: MarketplaceResourceId

      /// Whether this is an annual or a monthly plan.
      RenewalInterval: RenewalInterval

      /// When a certain plan was purchased
      SubscriptionStart: MeteringDateTime }

module Subscription =
    open NodaTime

    let updateBillingDimensions (dimensions: BillingDimensions) (subscription: Subscription) : Subscription =
        { subscription with Plan = subscription.Plan |> Plan.setBillingDimensions dimensions }

    let areDifferentBillingCycles (latestUpdate: MeteringDateTime) (now: MeteringDateTime) (subscription: Subscription) : bool =
        let getBillingCycle ((renewalInterval, subscriptionStart): RenewalInterval*MeteringDateTime) (idx: int) : (MeteringDateTime * MeteringDateTime) =
            let midnight (x: LocalDate) = $"%04d{x.Year}-%02d{x.Month}-%02d{x.Day}T00:00:00Z" |> MeteringDateTime.fromStr

            let startLocalDate = subscriptionStart.Date.Plus(renewalInterval.Multiply((uint)idx))
            let endLocalDate = subscriptionStart.Date.Plus(renewalInterval.Multiply((uint)idx + 1ul))
            let start = startLocalDate |> midnight
            let endDay = endLocalDate |> midnight

            (start, endDay)

        let IsInBillingCycle (value: MeteringDateTime) ((startDay, endDay): (MeteringDateTime * MeteringDateTime)) : bool =
            let (startDay, value, endDay) = (startDay.ToDateTimeUtc(), value.ToDateTimeUtc(), endDay.ToDateTimeUtc())
            let isIn = startDay <= value && value < endDay
            isIn

        let renewalInterval = subscription.RenewalInterval
        let subscriptionStart = subscription.SubscriptionStart

        let allBillingCycleToInfinity =
            Seq.initInfinite (getBillingCycle (renewalInterval, subscriptionStart))

        if now.ToDateTimeUtc() < subscriptionStart.ToDateTimeUtc()
        then failwithf "The point in time %A is before the subscription %A started" now subscriptionStart
        else
            let (startDate, endDate) = allBillingCycleToInfinity |> Seq.find (IsInBillingCycle now)

            let isIn = IsInBillingCycle latestUpdate (startDate, endDate)
            not isIn
