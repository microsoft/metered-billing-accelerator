namespace Metering.Types

open NodaTime
open Metering.Types

type BusinessLogic = // The business logic takes the current state, and a command to be applied, and returns new state
    Meter option -> MeteringEvent -> Meter option 

module DummyLogic =
    let private testAPI connections =
        let config =
            { CurrentTimeProvider = "2021-10-27--21-35-00" |> MeteringDateTime.fromStr |> CurrentTimeProvider.AlwaysReturnSameTime
              SubmitMeteringAPIUsageEvent = SubmitMeteringAPIUsageEvent.Discard
              GracePeriod = Duration.FromHours(6.0)
              ManagedResourceGroupResolver = ManagedAppResourceGroupID.retrieveDummyID "/subscriptions/deadbeef-stuff/resourceGroups/somerg"
              MeteringConnections = connections }
        let inputs : (MeteringEvent list) = []
        let state = MeterCollection.empty
        let result = inputs |> MeterCollection.meterCollectionHandleMeteringEvents config state
        result