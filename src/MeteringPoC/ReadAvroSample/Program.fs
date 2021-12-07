open System.Threading
open NodaTime
open Metering
open Metering.Types
open Metering.Types.EventHub

let config = 
    { CurrentTimeProvider = CurrentTimeProvider.LocalSystem
      SubmitMeteringAPIUsageEvent = SubmitMeteringAPIUsageEvent.Discard
      GracePeriod = Duration.FromHours(6.0)
      ManagedResourceGroupResolver = ManagedAppResourceGroupID.retrieveDummyID "/subscriptions/deadbeef-stuff/resourceGroups/somerg"
      MeteringConnections = MeteringConnections.getFromEnvironment() }

let partitionId = "0"

let latestState =
    config.MeteringConnections
    |> CaptureProcessor.readCaptureFromPosition CancellationToken.None
    |> Seq.map (EventHubEvent.createFromEventData partitionId EventHubObservableClient.toMeteringUpdateEvent)
    |> Seq.choose id
    |> Seq.map (fun e -> MeteringEvent.create e.EventData e.MessagePosition e.EventsToCatchup)
    |> Seq.map (fun e ->
        match e.MeteringUpdateEvent with
        | UsageReported e -> 
            printfn "%s %s %s"
                (e.InternalResourceId |> InternalResourceId.toStr)
                (e.MeterName |> ApplicationInternalMeterName.value)
                (e.Quantity |> Quantity.toStr)
        | _ -> ()

        e
    )
    |> Seq.scan (MeterCollectionLogic.handleMeteringEvent config) MeterCollection.Empty 
    |> Seq.map (fun e -> 
        //printfn "########################################################################################################################################################"
        //e |> Some |> MeterCollection.toStr |> printfn "%A"
        e
    )
    |> Seq.last

printfn "%s" (latestState |> Json.toStr 2)
printfn "########################################################################################################################################################"
printfn "%s" (MeterCollection.toStr (Some latestState))
