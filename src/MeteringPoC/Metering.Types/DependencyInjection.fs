namespace Metering.Types

open System
open System.Threading.Tasks
open NodaTime

type CurrentTimeProvider =
    unit -> MeteringDateTime

module CurrentTimeProvider =
    let LocalSystem : CurrentTimeProvider = (fun () -> ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc))
    let AlwaysReturnSameTime (time : MeteringDateTime) : CurrentTimeProvider = (fun () -> time)

type SubmitMeteringAPIUsageEvent = MeteringConfigurationProvider -> (MarketplaceRequest list) -> Task<MarketplaceBatchResponse> 
and MeteringConfigurationProvider = 
    { CurrentTimeProvider: CurrentTimeProvider
      SubmitMeteringAPIUsageEvent: SubmitMeteringAPIUsageEvent 
      GracePeriod: Duration
      //ManagedResourceGroupResolver: DetermineManagedAppResourceGroupID
      MeteringConnections: MeteringConnections }

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

module MeteringConfigurationProvider =
    let create (connections: MeteringConnections) (marketplaceClient: SubmitMeteringAPIUsageEvent) : MeteringConfigurationProvider =
        { CurrentTimeProvider = CurrentTimeProvider.LocalSystem
          SubmitMeteringAPIUsageEvent = marketplaceClient
          GracePeriod = Duration.FromHours(2.0)
          // ManagedResourceGroupResolver = ManagedAppResourceGroupID.retrieveManagedByFromARM
          MeteringConnections = connections }