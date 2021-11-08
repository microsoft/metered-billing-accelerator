namespace Metering.Types

open System.Threading.Tasks

/// Unique identifier of the resource against which usage is emitted. 
type MarketplaceResourceID = // https://docs.microsoft.com/en-us/azure/marketplace/marketplace-metering-service-apis#metered-billing-single-usage-event
    /// For Azure Application Managed Apps plans, the resourceId is the Managed App resource group Id. 
    | ManagedAppResourceGroupID of ManagedAppResourceGroupID
    
    /// For SaaS offers, the resourceId is the SaaS subscription ID. 
    | SaaSSubscriptionID of SaaSSubscriptionID

type ResourceIdConverter = InternalResourceId -> Task<MarketplaceResourceID>

module MarketplaceResourceID =
    let createFromManagedAppResourceGroupID x = x |> ManagedAppResourceGroupID.create |> ManagedAppResourceGroupID

    let createFromSaaSSubscriptionID x = x |> SaaSSubscriptionID.create |> SaaSSubscriptionID

    let toExternalResourceID (resolver: DetermineManagedAppResourceGroupID) : ResourceIdConverter =
        let converter (st: InternalResourceId) =
            match st with
            | ManagedApp -> 
                task {
                    let! x = resolver
                
                    return 
                        x
                        |> ManagedAppResourceGroupID.value
                        |> createFromManagedAppResourceGroupID
                }

            | SaaSSubscription s -> 
                let resourceId = 
                    s
                    |> SaaSSubscriptionID.value
                    |> createFromSaaSSubscriptionID 

                task { return resourceId }

        converter
