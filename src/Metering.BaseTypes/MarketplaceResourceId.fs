// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

// https://techcommunity.microsoft.com/t5/fasttrack-for-azure/azure-marketplace-metered-billing-picking-the-correct-id-when/ba-p/3542373

/// This is the key by which to aggregate across multiple tenants.
/// When submitting to Azure Marketplace API, the request can contain a resourceId, or a resourceUri, or even both.
type MarketplaceResourceId =
    private 
        { ResourceURI: string option
          ResourceID: string option }

    static member private requiredPrefixForResourceUris = "/subscriptions/"

    override this.ToString() =
        match (this.ResourceURI, this.ResourceID) with
        | (Some uri, _ )-> uri
        | (None, Some resourceId) -> resourceId
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

module MarketplaceResourceId =
    let addResourceId (resourceId: string) (marketplaceResourceId: MarketplaceResourceId) : MarketplaceResourceId =
        { marketplaceResourceId with ResourceID = Some resourceId }

    let addResourceUri (resourceUri: string) (marketplaceResourceId: MarketplaceResourceId) : MarketplaceResourceId =
        { marketplaceResourceId with ResourceURI = Some resourceUri }
