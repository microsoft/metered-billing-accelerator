// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

// https://techcommunity.microsoft.com/t5/fasttrack-for-azure/azure-marketplace-metered-billing-picking-the-correct-id-when/ba-p/3542373

type MarketplaceResourceIdError =
    /// Cannot merge mutually exclusive items. If one only has the resourceId and the other only has the resourceUri, we can't be sure they belong together.
    | NotEnoughInformationToMerge
    /// The two items have nothing in common.
    | MismatchingArgs
    /// One or more arguments were completely uninitialized.
    | UninitializedArgs

/// This is the key by which to aggregate across multiple tenants.
/// When submitting to Azure Marketplace API, the request can contain a resourceId, or a resourceUri, or even both.

type MarketplaceResourceId =
    private 
        { ResourceURI: string option
          ResourceID: string option }

    member this.ResourceUri() = this.ResourceURI |> Option.defaultWith (fun () -> null)
    member this.ResourceId() = this.ResourceID |> Option.defaultWith (fun () -> null)

    member this.Matches other = 
        let { ResourceURI = tu; ResourceID = ti } = this
        let { ResourceURI = ou; ResourceID = ii } = other

        match (tu, ti, ou, ii) with
        | (Some tu, _, Some ou, _) -> tu = ou // this' and the other's resourceUri are the same
        | (_, Some ti, _, Some ii) -> ti = ii // this' and the other's resourceId are the same
        | _ -> false

    //interface IEquatable<MarketplaceResourceId> with
    //    member this.Equals other = 
    //        let { ResourceURI = tu; ResourceID = ti } = this
    //        let { ResourceURI = ou; ResourceID = ii } = other
    //        tu = ou || ti = ii
    //override this.Equals other =
    //     match other with
    //     | :? MarketplaceResourceId as p -> (this :> IEquatable<_>).Equals p
    //     | _ -> false
    // override this.GetHashCode () = 
    //    (this.ResourceURI, this.ResourceID).GetHashCode()

    // This function merges two MarketplaceResourceIds, if possible.
    member this.Merge(other: MarketplaceResourceId) : Result<MarketplaceResourceId, MarketplaceResourceIdError> =
        let { ResourceURI = tu; ResourceID = ti } = this
        let { ResourceURI = ou; ResourceID = ii } = other

        match (tu, ti, ou, ii) with
        | (None,    None,    _,       _      ) -> Error(UninitializedArgs)
        | (_,       _,       None,    None   ) -> Error(UninitializedArgs)
        | (None,    Some _,  Some _,  None   ) -> Error(NotEnoughInformationToMerge)
        | (Some _,  None,    None,    Some _ ) -> Error(NotEnoughInformationToMerge)
        | (None,    Some ti, _,       Some ii) when ti = ii -> Ok(this)
        | (Some tu, _,       Some ou, None   ) when tu = ou -> Ok(this)
        | (Some tu, None,    Some ou, _      ) when tu = ou -> Ok(other)
        | (Some _,  Some ti, None,    Some ii) when ti = ii -> Ok(this)
        | (Some tu, Some ti, Some ou, Some ii) when tu = ou && ti = ii -> Ok(this)
        | _ -> Error(MismatchingArgs)

    static member private requiredPrefixForResourceUris = "/subscriptions/"

    override this.ToString() =
        match (this.ResourceURI, this.ResourceID) with
        | (Some uri, None)-> $"resourceUri=\"{uri}\""
        | (Some uri, Some resourceId )-> $"resourceId=\"{resourceId}\" / resourceUri=\"{uri}\""
        | (None, Some resourceId) -> $"resourceId=\"{resourceId}\""
        | (None, None) -> failwith "Missing id"
 
    static member from resourceUri resourceId = { ResourceURI = Some resourceUri; ResourceID = Some resourceId }

    static member fromResourceURI (resourceUri: string) = 
        if resourceUri.StartsWith(MarketplaceResourceId.requiredPrefixForResourceUris)
        then { ResourceURI = Some resourceUri; ResourceID = None }
        else raise (new System.ArgumentException(message = $"String must start with {MarketplaceResourceId.requiredPrefixForResourceUris}", paramName = nameof(resourceUri)))

    static member fromResourceID resourceId = { ResourceURI = None; ResourceID = Some resourceId }

    static member fromStr (s: string) : MarketplaceResourceId =
        if s.StartsWith(MarketplaceResourceId.requiredPrefixForResourceUris)
        then s |> MarketplaceResourceId.fromResourceURI
        else s |> MarketplaceResourceId.fromResourceID

    member this.addResourceId (resourceId: string) : MarketplaceResourceId =
        { this with ResourceID = Some resourceId }

    member this.addResourceUri (resourceUri: string) : MarketplaceResourceId =
        { this with ResourceURI = Some resourceUri }
