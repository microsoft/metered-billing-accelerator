namespace Metering.Types

/// Unique identifier of the resource against which usage is emitted. 
type ResourceID = // https://docs.microsoft.com/en-us/azure/marketplace/marketplace-metering-service-apis#metered-billing-single-usage-event
    | ManagedAppResourceGroupID of ManagedAppResourceGroupID
    | SaaSSubscriptionID of SaaSSubscriptionID

module ResourceID =
    let createFromManagedAppResourceGroupID x = x |> ManagedAppResourceGroupID.create |> ManagedAppResourceGroupID

    let createFromSaaSSubscriptionID x = x |> SaaSSubscriptionID.create |> SaaSSubscriptionID

