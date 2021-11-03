namespace Metering.Types

open NodaTime
open System.Threading.Tasks

type MeteringClient = 
    InternalUsageEvent -> Task

type SubmitMeteringAPIUsageEvent =
    MeteringAPIUsageEventDefinition -> Async<unit>

module SubmitMeteringAPIUsageEvent =
    let Discard : SubmitMeteringAPIUsageEvent = (fun _ -> Async.AwaitTask Task.CompletedTask)

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
