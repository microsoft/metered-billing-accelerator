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

module MeteringAPICredentials =
    let createManagedIdentity () : MeteringAPICredentials = 
        ManagedIdentity

    let createServicePrincipal tenantId clientId clientSecret : MeteringAPICredentials =
        { ClientId = clientId
          ClientSecret = clientSecret
          TenantId = tenantId } |> ServicePrincipalCredential
