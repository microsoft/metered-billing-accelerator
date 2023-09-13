// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open Metering.BaseTypes.EventHub

type SubscriptionUpdate =
    {
        /// The SaaS subscription ID or managed app ID.
        MarketplaceResourceId: MarketplaceResourceId

        /// The updated plan.
        UpdatedPlan: Plan
    }

module SubscriptionUpdate =
    // When we update a subscription, we must keep resourceID and start time the same.
    // We must determine which dimensions are new, which are updated, and which are deleted.

    let updatePlan (messagePosition: MessagePosition) (update: SubscriptionUpdate) (oldSubscription: Subscription) : Subscription =
        let oldDimensions = oldSubscription.Plan.BillingDimensions
        let newDimensions = update.UpdatedPlan.BillingDimensions

        raise (new System.NotImplementedException("SubscriptionUpdated is not implemented yet"))
