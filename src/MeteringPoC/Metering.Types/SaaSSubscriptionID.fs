namespace Metering.Types

/// For SaaS offers, the resourceId is the SaaS subscription ID. 
type SaaSSubscriptionID = private SaaSSubscriptionID of string

module SaaSSubscriptionID =
    let create x = (SaaSSubscriptionID x)
    let value (SaaSSubscriptionID x) = x
