namespace Metering.Types

/// Event representing the creation of a subscription. 
type SubscriptionCreationInformation =
    { Subscription: Subscription // The purchase information of the subscription
      InternalMetersMapping: InternalMetersMapping } // The table mapping app-internal meter names to 'proper' ones for marketplace
        
module SubscriptionCreationInformation =
    let toStr { Subscription = s } : string =
        $"{s.InternalResourceId} {s.RenewalInterval} subscribed {s.SubscriptionStart}"
        