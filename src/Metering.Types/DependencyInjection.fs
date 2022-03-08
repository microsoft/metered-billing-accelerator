// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Types

open System
open System.Threading.Tasks
open NodaTime

type SubmitMeteringAPIUsageEvent = MeteringConfigurationProvider -> (MarketplaceRequest list) -> Task<MarketplaceBatchResponse> 
and MeteringConfigurationProvider = 
    { CurrentTimeProvider: CurrentTimeProvider
      SubmitMeteringAPIUsageEvent: SubmitMeteringAPIUsageEvent 
      GracePeriod: Duration
      MeteringConnections: MeteringConnections }

module MeteringConfigurationProvider =
    let create (connections: MeteringConnections) (marketplaceClient: SubmitMeteringAPIUsageEvent) : MeteringConfigurationProvider =
        { CurrentTimeProvider = CurrentTimeProvider.LocalSystem
          SubmitMeteringAPIUsageEvent = marketplaceClient
          GracePeriod = Duration.FromHours(2.0)
          MeteringConnections = connections }

module SubmitMeteringAPIUsageEvent =
    let PretendEverythingIsAccepted : SubmitMeteringAPIUsageEvent = (fun _cfg requests -> 
        let headers = 
            { RequestID = Guid.NewGuid().ToString()
              CorrelationID = Guid.NewGuid().ToString() } 

        let messageTime = MeteringDateTime.now()
        let resourceUri = Some "/subscriptions/..../resourceGroups/.../providers/Microsoft.SaaS/resources/SaaS Accelerator Test Subscription"
        let newUsageEvent () = Some (Guid.Empty.ToString())
        requests
        |> List.map (fun request -> 
            { Headers = headers
              Result = Ok { RequestData = request; Status = { Status = Accepted; MessageTime = messageTime; UsageEventID = newUsageEvent(); ResourceURI = resourceUri } } } 
        )
        |> MarketplaceBatchResponse.create
        |> Task.FromResult
     )
