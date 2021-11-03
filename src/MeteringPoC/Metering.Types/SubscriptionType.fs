namespace Metering.Types

/// This is the key by which to aggregate across multiple tenants
type SubscriptionType =
    | ManagedApp
    | SaaSSubscription of SaaSSubscriptionID

module SubscriptionType =
    let private ManagedAppMarkerString = "AzureManagedApplication"

    let fromStr s =
        if ManagedAppMarkerString.Equals(s)
        then ManagedApp
        else s |> SaaSSubscriptionID.create |> SaaSSubscription

    let toStr = 
        function
        | ManagedApp -> ManagedAppMarkerString
        | SaaSSubscription x -> x |> SaaSSubscriptionID.value
