// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Types

open System.Threading.Tasks
open Thoth.Json.Net

/// For SaaS offers, the resourceId is the SaaS subscription ID. 
type SaaSSubscriptionID = private SaaSSubscriptionID of string

module SaaSSubscriptionID =
    let create x = (SaaSSubscriptionID x)
    let value (SaaSSubscriptionID x) = x

type ManagedApp =
    /// Internally used handle for a managed app
    | ManagedAppIdentity
    
    /// Concrete value of the managed app resource group to report against
    | ManagedAppResourceGroupID of string /// "/subscriptions/..../resourceGroups/.../providers/Microsoft.SaaS/resources/SaaS Accelerator Test Subscription"

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
    
    /// Retrieves the resource group's ResourceGroup.ManagedBy property from Azure Resource Manager.
    let retrieveManagedByFromARM (currentId: InternalResourceId) : Task<InternalResourceId> =
        match currentId with
        | SaaSSubscription _ -> currentId |> Task.FromResult
        | ManagedApplication m ->
            match m with
            | ManagedAppResourceGroupID _ -> currentId |> Task.FromResult
            | ManagedAppIdentity ->
                let RecourceGroupIdDecoder : Decoder<string>  =
                    Decode.object (fun get -> 
                        // tap into JSON path './compute/{x}'
                        let get x = get.Required.At [ "compute"; x ] Decode.string

                        $"""/subscriptions/{get "subscriptionId"}/resourceGroups/{get "resourceGroupName"}"""
                    )

                let ManagedByDecoder : Decoder<string> =
                    Decode.object (fun get -> get.Required.At [ "managedBy" ] Decode.string )

                task {
                    // Determine the resource ID we're running in, using the instance metadata endpoint
                    let c = InstanceMetadataClient.clientWithMetadataTrue "http://169.254.169.254/"
                    let! imdsJson = c.GetStringAsync "metadata/instance?api-version=2021-02-01" // TODO do we need &format=json as well ?
                    let resourceGroupId = 
                        match Decode.fromString RecourceGroupIdDecoder imdsJson with
                        | Ok x -> x
                        | Error e -> failwith e

                    let! armClient = InstanceMetadataClient.createArmClient()
                    let! armResponse = armClient.GetStringAsync $"{resourceGroupId}?api-version=2019-11-01"  // or 2019-07-01?
                    let managedBy = 
                        match Decode.fromString ManagedByDecoder armResponse with
                        | Ok x -> x
                        | Error e -> failwith e

                    return managedBy |> ManagedAppResourceGroupID |> ManagedApplication
                }

    let retrieveDummyID (dummyValue: string) : Task<InternalResourceId> =
        dummyValue |> ManagedAppResourceGroupID |> ManagedApplication |> Task.FromResult

