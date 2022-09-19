// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

// https://techcommunity.microsoft.com/t5/fasttrack-for-azure/azure-marketplace-metered-billing-picking-the-correct-id-when/ba-p/3542373

/// This is the key by which to aggregate across multiple tenants
type InternalResourceId =    
    | ResourceURI of string
    | ResourceID of string
    
    override this.ToString() =
        match this with
        | ResourceID resourceId -> resourceId
        | ResourceURI resourceUri -> resourceUri

    static member fromStr (s: string) : InternalResourceId =
        if s.StartsWith("/subscriptions/")
        then s |> ResourceURI
        else s |> ResourceID
