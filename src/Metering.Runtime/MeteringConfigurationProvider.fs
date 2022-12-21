// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Integration

open System.Threading.Tasks
open Metering.BaseTypes

type SubmitMeteringAPIUsageEvent = MeteringConfigurationProvider -> (MarketplaceRequest list) -> Task<MarketplaceBatchResponse>
and MeteringConfigurationProvider =
    { SubmitMeteringAPIUsageEvent: SubmitMeteringAPIUsageEvent
      MeteringConnections: MeteringConnections }

module MeteringConfigurationProvider =
    let create (connections: MeteringConnections) (marketplaceClient: SubmitMeteringAPIUsageEvent) : MeteringConfigurationProvider =
        { SubmitMeteringAPIUsageEvent = marketplaceClient
          MeteringConnections = connections }
