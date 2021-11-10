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
        { Payload = e
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

type CurrentTimeProvider =
    unit -> MeteringDateTime

module CurrentTimeProvider =
    let LocalSystem : CurrentTimeProvider = (fun () -> ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc))
    let AlwaysReturnSameTime (time : MeteringDateTime) : CurrentTimeProvider = (fun () -> time)

type MeteringConfigurationProvider = 
    { CurrentTimeProvider: CurrentTimeProvider
      SubmitMeteringAPIUsageEvent: SubmitMeteringAPIUsageEvent 
      GracePeriod: Duration
      ManagedResourceGroupResolver: DetermineManagedAppResourceGroupID
      MeteringAPICredentials: MeteringAPICredentials }
