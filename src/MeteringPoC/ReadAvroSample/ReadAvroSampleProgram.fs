open System.Threading
open NodaTime
open Metering
open Metering.Types
open Metering.Types.EventHub

module MySeq =
    let inspect<'T> i =
        let inspect (f: 'T -> string option) a =
            match f a with 
            | Some s -> printfn "%s" s
            | None -> ()
            a
        Seq.map (inspect i)

let printme e = 
    match e.MeteringUpdateEvent with
    | UsageReported e -> 
        sprintf "%s %s %s"
            (e.InternalResourceId |> InternalResourceId.toStr)
            (e.MeterName |> ApplicationInternalMeterName.value)
            (e.Quantity |> Quantity.toStr)
        |> Some
    | _ -> None

let config : MeteringConfigurationProvider = 
    { CurrentTimeProvider = CurrentTimeProvider.LocalSystem
      SubmitMeteringAPIUsageEvent = SubmitMeteringAPIUsageEvent.Discard
      GracePeriod = Duration.FromHours(6.0)
      ManagedResourceGroupResolver = ManagedAppResourceGroupID.retrieveDummyID "/subscriptions/deadbeef-stuff/resourceGroups/somerg"
      MeteringConnections = MeteringConnections.getFromEnvironment() }

let partitionId = "0" |> PartitionID.create

// printfn "%A" ("meteringhack-standard.servicebus.windows.net/hub2/0/2021-12-06--21-01-33---sequencenr-31.json.gz" |> MeterCollectionStore.Naming.blobnameToPosition config)

let initialState = (MeterCollectionStore.loadStateFromFilename config partitionId CancellationToken.None "meteringhack-standard.servicebus.windows.net/hub2/0/2021-12-06--15-17-11---sequencenr-10.json.gz" ).Result
// let initialState = (MeterCollectionStore.loadLastState config partitionId CancellationToken.None).Result

match initialState with
| None -> 
    eprintfn "Could not load existing state"
    exit -1
| Some initialState -> 
    // let startPosition = (MessagePosition.createData partitionId 141 64576 (MeteringDateTime.fromStr "2021-12-07T18:55:38.6Z"))

    let aggregate = MeterCollectionLogic.handleMeteringEvent config
    let startPosition = initialState.LastUpdate.Value

    let x = 
        config.MeteringConnections
        |> CaptureProcessor.readEventsFromPosition EventHubObservableClient.toMeteringUpdateEvent startPosition CancellationToken.None 
        |> MySeq.inspect (fun me -> $"{me.Source |> EventSource.toStr} {me.MessagePosition.SequenceNumber} {me.MessagePosition.PartitionTimestamp} " |> Some)
        |> Seq.map (fun e -> MeteringEvent.create e.EventData e.MessagePosition e.EventsToCatchup)
        |> Seq.scan aggregate initialState
        |> Seq.last

    printf "%s" (x |> Some |> MeterCollection.toStr)
