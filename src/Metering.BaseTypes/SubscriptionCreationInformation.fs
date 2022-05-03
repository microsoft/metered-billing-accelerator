// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

/// Event representing the creation of a subscription. 
type SubscriptionCreationInformation =
    { Subscription: Subscription // The purchase information of the subscription
      InternalMetersMapping: InternalMetersMapping } // The table mapping app-internal meter names to 'proper' ones for marketplace

    override this.ToString() =
        $"{this.Subscription.SubscriptionStart |> MeteringDateTime.toStr}: SubscriptionCreation ID={this.Subscription.InternalResourceId.ToString()} {this.Subscription.RenewalInterval}"
