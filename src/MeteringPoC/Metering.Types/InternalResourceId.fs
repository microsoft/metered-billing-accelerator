namespace Metering.Types

/// For SaaS offers, the resourceId is the SaaS subscription ID. 
type SaaSSubscriptionID = private SaaSSubscriptionID of string

module SaaSSubscriptionID =
    let create x = (SaaSSubscriptionID x)
    let value (SaaSSubscriptionID x) = x

/// This is the key by which to aggregate across multiple tenants
type InternalResourceId =
    | ManagedApp
    | SaaSSubscription of SaaSSubscriptionID

module InternalResourceId =
    let private ManagedAppMarkerString = "AzureManagedApplication"

    let fromStr s =
        if ManagedAppMarkerString.Equals(s)
        then ManagedApp
        else s |> SaaSSubscriptionID.create |> SaaSSubscription

    let toStr = 
        function
        | ManagedApp -> ManagedAppMarkerString
        | SaaSSubscription x -> x |> SaaSSubscriptionID.value
