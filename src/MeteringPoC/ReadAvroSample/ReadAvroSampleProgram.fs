open System
open System.Threading
open NodaTime
open Metering
open Metering.Types
open Metering.Types.EventHub
open System.IO

module MySeq =
    let inspect<'T> i =
        let inspect (f: 'T -> string option) a =
            match f a with 
            | Some s -> printfn "%s" s
            | None -> ()
            a
        Seq.map (inspect i)

//let printme e = 
//    match e.MeteringUpdateEvent with
//    | UsageReported e -> 
//        sprintf "%s %s %s"
//            (e.InternalResourceId |> InternalResourceId.toStr)
//            (e.MeterName |> ApplicationInternalMeterName.value)
//            (e.Quantity |> Quantity.toStr)
//        |> Some
//    | _ -> None

let config : MeteringConfigurationProvider = 
    { CurrentTimeProvider = CurrentTimeProvider.LocalSystem
      SubmitMeteringAPIUsageEvent = SubmitMeteringAPIUsageEvent.PretendEverythingIsAccepted
      GracePeriod = Duration.FromHours(6.0)
      MeteringConnections = MeteringConnections.getFromEnvironment() }


//// Try to submit values
//File.ReadAllText("latest.json")
//|> Json.fromStr<MeterCollection>
//|> MeterCollection.metersToBeSubmitted
//|> Seq.sortBy (fun a -> a.EffectiveStartTime.ToInstant())
//|> Seq.skip 25
//|> Seq.take 25
//|> Seq.toList
//|> MarketplaceClient.submitBatchUsage config
//|> (fun x -> x.Result)
//|> (fun x -> 
//    let r = x |> Json.toStr 1
//    File.WriteAllText("response.json", r)
//    x
//    )
//|> (fun x -> x.Results)
//|> Seq.iter (fun a -> printfn "%A" a.Result)

let partitionId = "0" |> PartitionID.create

//// Create state prior certain timestamp
//let x = ManagementUtils.recreateStateFromEventHubCapture config (MessagePosition.createData "0" 482128 (MeteringDateTime.fromStr "2021-12-20T09:25:18Z")) 
//File.WriteAllText("482127.json", (x |> Json.toStr 2))
//exit 0


//// Echo messages from a point on
//ManagementUtils.showEventsFromPositionInEventHub config partitionId (MeteringDateTime.create 2021 12 20 06 00 00)
//|> Seq.iter (fun x -> 
//    match x.EventData with
//    | UsageSubmittedToAPI usage -> 
//        printfn "%d %s" x.MessagePosition.SequenceNumber (x.MessagePosition.PartitionTimestamp |> MeteringDateTime.toStr)
//        // printfn "%d: %s\n\n" x.MessagePosition.SequenceNumber (usage.Result |> Json.toStr 0)
//    | _ -> ()
//)
//exit 0





//let rnd = Random()
//let bytes = Array.create 16 0uy
//rnd.NextBytes(bytes)

//let j = "[{\"type\":\"UsageReported\",\"value\":{\"internalResourceId\":\"2b196a35-1379-4cb0-5457-4d18c28d46e6\",\"timestamp\":\"2021-12-10T14:29:10.0995601Z\",\"meterName\":\"nde\",\"quantity\":\"20\",\"properties\":{}}},{\"type\":\"UsageReported\",\"value\":{\"internalResourceId\":\"2b196a35-1379-4cb0-5457-4d18c28d46e6\",\"timestamp\":\"2021-12-10T14:29:10.113733Z\",\"meterName\":\"cpu\",\"quantity\":\"20\",\"properties\":{}}},{\"type\":\"UsageReported\",\"value\":{\"internalResourceId\":\"2b196a35-1379-4cb0-5457-4d18c28d46e6\",\"timestamp\":\"2021-12-10T14:29:10.1137354Z\",\"meterName\":\"dta\",\"quantity\":\"20\",\"properties\":{}}},{\"type\":\"UsageReported\",\"value\":{\"internalResourceId\":\"2b196a35-1379-4cb0-5457-4d18c28d46e6\",\"timestamp\":\"2021-12-10T14:29:10.1137357Z\",\"meterName\":\"msg\",\"quantity\":\"20\",\"properties\":{}}},{\"type\":\"UsageReported\",\"value\":{\"internalResourceId\":\"2b196a35-1379-4cb0-5457-4d18c28d46e6\",\"timestamp\":\"2021-12-10T14:29:10.1137359Z\",\"meterName\":\"obj\",\"quantity\":\"20\",\"properties\":{}}}]"
//let bytes = System.Text.Encoding.UTF8.GetBytes(j)
//let ed = EventDataDummy.create "1.avro" bytes 13L 100L "0"
//let ue = ed |> EventHubObservableClient.toMeteringUpdateEvent 
//let s = ue |> Json.toStr 1
//let s2 = s |> Json.fromStr<MeteringUpdateEvent>

// Delete event 59 from state
//(ClientSDK.MeteringEventHubExtensions.RemoveUnprocessableMessagesUpTo 
//    (config.MeteringConnections |> MeteringConnections.createEventHubProducerClient)
//    (partitionId) 200000 CancellationToken.None).Wait()

// printfn "%A" ("meteringhack-standard.servicebus.windows.net/hub2/0/2021-12-06--21-01-33---sequencenr-31.json.gz" |> MeterCollectionStore.Naming.blobnameToPosition config)

// let initialState = (MeterCollectionStore.loadStateFromFilename config partitionId CancellationToken.None "meteringhack-standard.servicebus.windows.net/hub2/0/2021-12-06--15-17-11---sequencenr-10.json.gz" ).Result
// let initialState = (MeterCollectionStore.loadStateFromFilename config partitionId CancellationToken.None "meteringhack-standard.servicebus.windows.net/hub2/0/latest.json.gz" ).Result
// let initialState = (MeterCollectionStore.loadLastState config partitionId CancellationToken.None).Result
// let initialState : MeterCollection option = MeterCollection.Uninitialized
let initialState = File.ReadAllText("C:\\Users\\chgeuer\\Desktop\\482127.json") |> Json.fromStr<MeterCollection> |> Some


match initialState with
| None -> 
    let aggregate = MeterCollectionLogic.handleMeteringEvent config
    let partitionId = "0"
    let x = 
        config.MeteringConnections
        |> CaptureProcessor.readAllEvents EventHubObservableClient.toMeteringUpdateEvent partitionId CancellationToken.None 
        //|> MySeq.inspect (fun me -> $"{me.Source |> EventSource.toStr} {me.MessagePosition.SequenceNumber} {me.MessagePosition.PartitionTimestamp} " |> Some)
        |> Seq.map MeteringEvent.fromEventHubEvent
        |> Seq.scan aggregate MeterCollection.Empty
        |> Seq.last

    //printfn "%s" (x |> Some |> MeterCollection.toStr)
    //printfn "%s" (x |> Json.toStr 2)

    File.WriteAllText("latest.json", x |> Json.toStr 1)

    (MeterCollectionStore.storeLastState config x CancellationToken.None).Wait()

    let x =
        File.ReadAllText("latest.json")
        |> Json.fromStr<MeterCollection>

    x
    |> MeterCollection.metersToBeSubmitted
    |> Seq.sortBy (fun a -> a.EffectiveStartTime.ToInstant())
    |> Seq.iter (fun a -> printfn "%s %s %s/%s %s" (a.EffectiveStartTime |> MeteringDateTime.toStr) (a.ResourceId |> InternalResourceId.toStr) (a.PlanId |> PlanId.value) (a.DimensionId |> DimensionId.value) (a.Quantity |> Quantity.toStr)  )

| Some initialState -> 
    // let startPosition = (MessagePosition.createData partitionId 141 64576 (MeteringDateTime.fromStr "2021-12-07T18:55:38.6Z"))

    let aggregate = MeterCollectionLogic.handleMeteringEvent config
    let startPosition = initialState.LastUpdate.Value

    let x = 
        config.MeteringConnections
        |> CaptureProcessor.readEventsFromPosition EventHubObservableClient.toMeteringUpdateEvent startPosition CancellationToken.None 
        // |> MySeq.inspect (fun me -> $"{me.Source |> EventSource.toStr} {me.MessagePosition.SequenceNumber} {me.MessagePosition.PartitionTimestamp} " |> Some)
        |> Seq.map MeteringEvent.fromEventHubEvent
        |> Seq.scan aggregate initialState
        |> Seq.last

    // printfn "%s" (x |> Some |> MeterCollection.toStr)

    printfn "%s" (x |> Json.toStr 2)

    x
    |> MeterCollection.metersToBeSubmitted
    |> Seq.sortBy (fun a -> a.EffectiveStartTime.ToInstant())
    |> Seq.iter (fun a -> printfn "%s %s %s/%s %s" (a.EffectiveStartTime |> MeteringDateTime.toStr) (a.ResourceId |> InternalResourceId.toStr) (a.PlanId |> PlanId.value) (a.DimensionId |> DimensionId.value) (a.Quantity |> Quantity.toStr)  )
