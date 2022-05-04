// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Integration

type ServicePrincipalCredential = 
    { ClientId: string 
      ClientSecret: string
      TenantId: string }

type MeteringAPICredentials =
    | ManagedIdentity
    | ServicePrincipalCredential of ServicePrincipalCredential

    static member createManagedIdentity () : MeteringAPICredentials = 
        ManagedIdentity
 
    static member createServicePrincipal tenantId clientId clientSecret : MeteringAPICredentials =
        { ClientId = clientId
          ClientSecret = clientSecret
          TenantId = tenantId } |> ServicePrincipalCredential
