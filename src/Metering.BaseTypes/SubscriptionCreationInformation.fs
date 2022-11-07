// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

/// Event representing the creation of a subscription. 
type SubscriptionCreationInformation =
    { Subscription: Subscription } // The purchase information of the subscription

    override this.ToString() =
        $"{this.Subscription.SubscriptionStart |> MeteringDateTime.toStr}: SubscriptionCreation ID={this.Subscription.MarketplaceResourceId.ToString()} {this.Subscription.RenewalInterval}"
