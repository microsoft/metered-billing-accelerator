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

let config = 
    { CurrentTimeProvider = CurrentTimeProvider.LocalSystem
      SubmitMeteringAPIUsageEvent = SubmitMeteringAPIUsageEvent.Discard
      GracePeriod = Duration.FromHours(6.0)
      ManagedResourceGroupResolver = ManagedAppResourceGroupID.retrieveDummyID "/subscriptions/deadbeef-stuff/resourceGroups/somerg"
      MeteringConnections = MeteringConnections.getFromEnvironment() }

let partitionId = "0"

//let latestState =
//    config.MeteringConnections
//    // |> CaptureProcessor.readCaptureFromPosition CancellationToken.None
//    |> CaptureProcessor.readEventsFromPosition CancellationToken.None (MessagePosition.createData partitionId 141 64576 (MeteringDateTime.fromStr "2021-12-07T18:55:38.6Z"))
//    |> Seq.map (EventHubEvent.createFromEventData partitionId EventHubObservableClient.toMeteringUpdateEvent)
//    |> Seq.choose id
//    |> Seq.map (fun e -> MeteringEvent.create e.EventData e.MessagePosition e.EventsToCatchup)
//    // |> MySeq.inspect printme
//    |> Seq.scan (MeterCollectionLogic.handleMeteringEvent config) MeterCollection.Empty
//    // |> MySeq.inspect (Some >> MeterCollection.toStr >> Some)
//    |> Seq.last

//printfn "%s" (latestState |> Json.toStr 0)
//printfn "#########################################################################################"
//printfn "%s" (latestState |> Some |> MeterCollection.toStr)

let startPosition = (MessagePosition.createData partitionId 141 64576 (MeteringDateTime.fromStr "2021-12-07T18:55:38.6Z"))

let x = 
    config.MeteringConnections
    |> CaptureProcessor.readEventsFromPosition EventHubObservableClient.toMeteringUpdateEvent startPosition CancellationToken.None 
    |> MySeq.inspect (fun me -> $"{me.Source |> EventSource.toStr} {me.MessagePosition.SequenceNumber} {me.MessagePosition.PartitionTimestamp} " |> Some)

let r = sprintf "Processed %d events" (x |> Seq.length)
