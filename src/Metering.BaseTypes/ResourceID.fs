// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

// https://techcommunity.microsoft.com/t5/fasttrack-for-azure/azure-marketplace-metered-billing-picking-the-correct-id-when/ba-p/3542373

/// This is the key by which to aggregate across multiple tenants
type InternalResourceId =
    { 
        ResourceURI: string option
        ResourceID: string option
    }

    override this.ToString() =
        match (this.ResourceURI, this.ResourceID) with
        | (Some uri, _ )-> uri
        | (None, Some resourceId) -> resourceId
        | (None, None) -> failwith "Missing id"
 
    static member from resourceUri resourceId = { ResourceURI = Some resourceUri; ResourceID = Some resourceId }

    static member fromResourceURI resourceUri = { ResourceURI = Some resourceUri; ResourceID = None }

    static member fromResourceID resourceId = { ResourceURI = None; ResourceID = Some resourceId }

    static member fromStr (s: string) : InternalResourceId =
        if s.StartsWith("/subscriptions/")
        then s |> InternalResourceId.fromResourceURI
        else s |> InternalResourceId.fromResourceID
