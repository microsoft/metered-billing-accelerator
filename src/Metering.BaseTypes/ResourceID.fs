// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

/// For SaaS offers, the resourceId is the SaaS subscription ID. 
type SaaSSubscriptionID = 
    { Value: string }

    static member create x = { SaaSSubscriptionID.Value = x }

type ManagedApp =
    /// Internally used handle for a managed app
    | ManagedAppIdentity
    
    /// Concrete value of the managed app resource group to report against
    | ManagedAppResourceGroupID of string // "/subscriptions/..../resourceGroups/.../providers/Microsoft.SaaS/resources/SaaS Accelerator Test Subscription"

/// This is the key by which to aggregate across multiple tenants
type InternalResourceId =    
    | ManagedApplication of ManagedApp
    | SaaSSubscription of SaaSSubscriptionID
    
    override this.ToString() =
        match this with
        | SaaSSubscription saasId -> saasId.Value
        | ManagedApplication mid -> 
            match mid with 
            | ManagedAppResourceGroupID rgid -> rgid
            | ManagedAppIdentity -> "AzureManagedApplication"

    static member fromStr (s: string) : InternalResourceId =
        if "AzureManagedApplication".Equals(s)
        then ManagedAppIdentity |> ManagedApplication
        else
            if s.StartsWith("/subscriptions/")
            then s |> ManagedAppResourceGroupID |> ManagedApplication
            else s |> SaaSSubscriptionID.create |> SaaSSubscription
