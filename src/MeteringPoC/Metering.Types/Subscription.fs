namespace Metering.Types

type Subscription = 
    { Plan: Plan
      SubscriptionType: SubscriptionType
      RenewalInterval: RenewalInterval 
      SubscriptionStart: MeteringDateTime } // When a certain plan was purchased

module Subscription =
    let create plan subType pri subscriptionStart =
        { Plan = plan
          SubscriptionType = subType
          RenewalInterval = pri
          SubscriptionStart = subscriptionStart }

