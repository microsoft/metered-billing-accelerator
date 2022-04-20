// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

type Subscription = 
    { /// The details of the plan
      Plan: Plan

      /// The SaaS subscription ID or managed app ID.
      InternalResourceId: InternalResourceId

      /// Whether this is an annual or a monthly plan.
      RenewalInterval: RenewalInterval 
      
      /// When a certain plan was purchased
      SubscriptionStart: MeteringDateTime }

module Subscription =
    let create plan internalResourceId renewalInterval subscriptionStart =
        { Plan = plan
          InternalResourceId = internalResourceId
          RenewalInterval = renewalInterval
          SubscriptionStart = subscriptionStart }
