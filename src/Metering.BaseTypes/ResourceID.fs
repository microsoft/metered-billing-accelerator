// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

/// For SaaS offers, the resourceId is the SaaS subscription ID. 
type SaaSSubscriptionID = private SaaSSubscriptionID of string

module SaaSSubscriptionID =
    let create x = (SaaSSubscriptionID x)
    let value (SaaSSubscriptionID x) = x

type ManagedApp =
    /// Internally used handle for a managed app
    | ManagedAppIdentity
    
    /// Concrete value of the managed app resource group to report against
    | ManagedAppResourceGroupID of string // "/subscriptions/..../resourceGroups/.../providers/Microsoft.SaaS/resources/SaaS Accelerator Test Subscription"

/// This is the key by which to aggregate across multiple tenants
type InternalResourceId =    
    | ManagedApplication of ManagedApp
    | SaaSSubscription of SaaSSubscriptionID

module InternalResourceId =
    // This marker is used to refer to a managed app, when there was not yet a lookup for the concrete ARM resource ID
    let private ManagedAppMarkerString = "AzureManagedApplication"

    let fromStr (s: string) : InternalResourceId =
        if ManagedAppMarkerString.Equals(s)
        then ManagedAppIdentity |> ManagedApplication
        else
            if s.StartsWith("/subscriptions/")
            then s |> ManagedAppResourceGroupID |> ManagedApplication
            else s |> SaaSSubscriptionID.create |> SaaSSubscription

    let toStr str = 
        match str with 
        | SaaSSubscription saasId -> saasId |> SaaSSubscriptionID.value
        | ManagedApplication mid -> 
            match mid with 
            | ManagedAppResourceGroupID rgid -> rgid
            | ManagedAppIdentity -> ManagedAppMarkerString

