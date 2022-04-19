// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

type Subscription = 
    { Plan: Plan
      InternalResourceId: InternalResourceId
      RenewalInterval: RenewalInterval 
      /// When a certain plan was purchased
      SubscriptionStart: MeteringDateTime }

module Subscription =
    let create plan internalResourceId pri subscriptionStart =
        { Plan = plan
          InternalResourceId = internalResourceId
          RenewalInterval = pri
          SubscriptionStart = subscriptionStart }
