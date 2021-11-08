namespace Metering.Types

open System
open System.Threading.Tasks
open NodaTime

type MeteringClient = 
    InternalUsageEvent -> Task

type SubmitMeteringAPIUsageEvent =
    MeteringAPIUsageEventDefinition -> Task<MarketplaceSubmissionResult>

module SubmitMeteringAPIUsageEvent =
    let Discard : SubmitMeteringAPIUsageEvent = (fun e -> 
        { ResourceID = e.ResourceId 
          Quantity = e.Quantity
          PlanId = e.PlanId
          DimensionId = e.DimensionId 
          EffectiveStartTime = e.EffectiveStartTime             
          UsageEventId = Guid.Empty.ToString()
          MessageTime = e.EffectiveStartTime
          ResourceURI = e.ResourceId |> InternalResourceId.toStr }
        |> Ok
        |> Task.FromResult
        )

type CurrentTimeProvider =
    unit -> MeteringDateTime

module CurrentTimeProvider =
    let LocalSystem : CurrentTimeProvider = (fun () -> ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc))
    let AlwaysReturnSameTime (time : MeteringDateTime) : CurrentTimeProvider = (fun () -> time)

type MeteringConfigurationProvider = 
    { CurrentTimeProvider: CurrentTimeProvider
      SubmitMeteringAPIUsageEvent: SubmitMeteringAPIUsageEvent 
      GracePeriod: Duration
      ManagedResourceGroupResolver: DetermineManagedAppResourceGroupID }
