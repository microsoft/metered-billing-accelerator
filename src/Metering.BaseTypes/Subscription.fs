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

    static member create plan marketplaceResourceId renewalInterval subscriptionStart =
        { Plan = plan
          MarketplaceResourceId = marketplaceResourceId
          RenewalInterval = renewalInterval
          SubscriptionStart = subscriptionStart }
    
    member this.updateBillingDimensions (dimensions: BillingDimensions) : Subscription =
        { this with Plan = this.Plan |> (Plan.updateBillingDimensions dimensions) }
