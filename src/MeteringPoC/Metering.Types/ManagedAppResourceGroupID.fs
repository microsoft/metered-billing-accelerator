namespace Metering.Types

/// For Azure Application Managed Apps plans, the resourceId is the Managed App resource group Id. +
type ManagedAppResourceGroupID = private ManagedAppResourceGroupID of string
    
module ManagedAppResourceGroupID =
    let value (ManagedAppResourceGroupID x) = x
    let create x = (ManagedAppResourceGroupID x)
