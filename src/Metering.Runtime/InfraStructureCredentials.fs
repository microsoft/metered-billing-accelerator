// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Integration

open Azure.Core
open Azure.Identity

module InfraStructureCredentials =
    let createManagedIdentity () : TokenCredential = 
        new DefaultAzureCredential()

    let createServicePrincipal tenantId clientId clientSecret : TokenCredential =
        new ClientSecretCredential(tenantId = tenantId, clientId = clientId, clientSecret = clientSecret)    
