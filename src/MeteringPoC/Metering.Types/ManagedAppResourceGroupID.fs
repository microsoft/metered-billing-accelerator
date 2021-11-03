namespace Metering.Types

/// For Azure Application Managed Apps plans, the resourceId is the Managed App resource group Id. +
type ManagedAppResourceGroupID = private ManagedAppResourceGroupID of string

type DetermineManagedAppResourceGroupID = Async<ManagedAppResourceGroupID>
    
module ManagedAppResourceGroupID =
    let value (ManagedAppResourceGroupID x) = x
    let create x = (ManagedAppResourceGroupID x)

    let retrieveProductionID : DetermineManagedAppResourceGroupID =
        async {
            return 
                "notimplementedarntiearsntieonaeit"
                |> create
        }

    let retrieveDummyID : DetermineManagedAppResourceGroupID =
        async {
            return 
                "https://management.azure.com/subscriptions/deadbeef-stuff-/resourceGroups/longterm"
                |> create
        }
