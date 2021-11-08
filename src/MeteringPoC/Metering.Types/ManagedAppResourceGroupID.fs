namespace Metering.Types

open System.Threading.Tasks
open Thoth.Json.Net
 
/// For Azure Application Managed Apps plans, the resourceId is the Managed App resource group Id. +
type ManagedAppResourceGroupID = private ManagedAppResourceGroupID of string

type DetermineManagedAppResourceGroupID = Task<ManagedAppResourceGroupID>

module ManagedAppResourceGroupID =
    let value (ManagedAppResourceGroupID x) = x
    let create x = (ManagedAppResourceGroupID x)

    let private RecourceGroupIdDecoder : Decoder<string>  =
        Decode.object (fun get -> 
            let s x = get.Required.At [ "compute"; x ] Decode.string
            $"""/subscriptions/{s "subscriptionId"}/resourceGroups/{s "resourceGroupName"}"""
        )

    let private ManagedByDecoder : Decoder<string> =
        Decode.object (fun get -> get.Required.At [ "managedBy" ] Decode.string )
   
    /// Retrieves the resource group's ResourceGroup.ManagedBy property from Azure Resource Manager.
    let retrieveManagedByFromARM : DetermineManagedAppResourceGroupID =
        task {
            // Determine the resource ID we're running in, using the instance metadata endpoint
            let c = InstanceMetadataClient.clientWithMetadataTrue "http://169.254.169.254/"
            let! imdsJson = c.GetStringAsync "metadata/instance?api-version=2021-02-01"
            let resourceGroupId = 
                match Decode.fromString RecourceGroupIdDecoder imdsJson with
                | Ok x -> x
                | Error e -> failwith e

            let! armClient = InstanceMetadataClient.createWithAccessToken "https://management.azure.com/"
            let! armResponse = armClient.GetStringAsync $"{resourceGroupId}?api-version=2019-11-01"  // or 2019-07-01?
            let managedBy = 
                match Decode.fromString ManagedByDecoder armResponse with
                | Ok x -> x
                | Error e -> failwith e

            return create managedBy
        }

    let retrieveDummyID (dummyValue: string) : DetermineManagedAppResourceGroupID =
        task {
            return dummyValue  |> create
        }
        
