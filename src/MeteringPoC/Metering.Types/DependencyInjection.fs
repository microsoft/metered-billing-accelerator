namespace Metering.Types

open System
open System.Threading.Tasks
open NodaTime
open Azure.Storage.Blobs

type MeteringClient = 
    InternalUsageEvent -> Task

type CurrentTimeProvider =
    unit -> MeteringDateTime

module CurrentTimeProvider =
    let LocalSystem : CurrentTimeProvider = (fun () -> ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc))
    let AlwaysReturnSameTime (time : MeteringDateTime) : CurrentTimeProvider = (fun () -> time)

type SubmitMeteringAPIUsageEvent = MeteringConfigurationProvider -> MeteringAPIUsageEventDefinition -> Task<MarketplaceSubmissionResult>
and MeteringConfigurationProvider = 
    { CurrentTimeProvider: CurrentTimeProvider
      SubmitMeteringAPIUsageEvent: SubmitMeteringAPIUsageEvent 
      GracePeriod: Duration
      ManagedResourceGroupResolver: DetermineManagedAppResourceGroupID
      MeteringConnections: MeteringConnections }

module SubmitMeteringAPIUsageEvent =
    let Discard : SubmitMeteringAPIUsageEvent = (fun _cfg e -> 
        { Payload = e
          Headers = 
            { RequestID = Guid.NewGuid().ToString()
              CorrelationID = Guid.NewGuid().ToString() }
          MarketplaceSubmissionResult.Result = 
            { UsageEventId = Guid.Empty.ToString()
              MessageTime = MeteringDateTime.now()
              Status = "Accepted"
              ResourceId = e.ResourceId |> InternalResourceId.toStr
              ResourceURI = "/subscriptions/..../resourceGroups/.../providers/Microsoft.SaaS/resources/SaaS Accelerator Test Subscription"
              Quantity = e.Quantity |> Quantity.createFloat
              DimensionId = e.DimensionId
              EffectiveStartTime = e.EffectiveStartTime
              PlanId = e.PlanId }
            |> Ok
        }
        |> Task.FromResult
     )

module MeteringConfigurationProvider =
    let create (connections: MeteringConnections) (marketplaceClient: SubmitMeteringAPIUsageEvent) : MeteringConfigurationProvider = 
        { CurrentTimeProvider = CurrentTimeProvider.LocalSystem
          SubmitMeteringAPIUsageEvent = marketplaceClient
          GracePeriod = Duration.FromHours(2.0)
          ManagedResourceGroupResolver = ManagedAppResourceGroupID.retrieveManagedByFromARM
          MeteringConnections = connections }