// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

open System.Threading
open System.IO
//open Azure.Messaging.EventHubs.Consumer
open Metering.BaseTypes
open Metering.BaseTypes.EventHub
open Metering.Integration
open Metering.EventHub

module MySeq =
    let inspect<'T> i =
        let inspect (f: 'T -> string option) a =
            match f a with 
            | Some s -> printfn "%s" s
            | None -> ()
            a
        Seq.map (inspect i)

let partitionId = "2" |> PartitionID.create
let connections = MeteringConnections.getFromEnvironment()

CaptureProcessor.readAllEvents 
    CaptureProcessor.toMeteringUpdateEvent
    partitionId
    CancellationToken.None
    connections
|> Seq.iter (fun i -> 
    let ts = (i.MessagePosition.PartitionTimestamp |> MeteringDateTime.toStr)
    match i.EventData with
    | UsageReported _ -> ()
    | SubscriptionPurchased sp -> 
        printfn "%s Subscription %s purchased" ts (sp.Subscription.MarketplaceResourceId.ToString())
    | SubscriptionDeletion _ -> ()
    | UnprocessableMessage _ -> ()
    | RemoveUnprocessedMessages _ -> ()
    | UsageSubmittedToAPI submitted -> 
        match submitted.Result with 
        | Ok success -> printfn "%s %s %s %s" (success.RequestData.EffectiveStartTime |> MeteringDateTime.toStr)  (success.Status.MessageTime |> MeteringDateTime.toStr) (success.RequestData.Quantity.ToString()) (success.RequestData.MarketplaceResourceId.ToString())
        | Error e -> 
            match e with 
            | DuplicateSubmission d -> eprintfn "%s Duplicate %s" ts (d.PreviouslyAcceptedMessage.RequestData.EffectiveStartTime |> MeteringDateTime.toStr)
            | ResourceNotFound r -> eprintfn "%s ResourceNotFound %s" ts (r.RequestData.MarketplaceResourceId.ToString())
            | Expired e -> eprintfn "%s Expired %s" ts (e.RequestData.EffectiveStartTime |> MeteringDateTime.toStr)
            | Generic g -> eprintfn "%s Error %A" ts g

    // | a -> printfn "%d %s" i.MessagePosition.SequenceNumber  (a |> MeteringUpdateEvent.toStr)s
)
exit 0

//let c = config.MeteringConnections |> MeteringConnections.createEventHubConsumerClient 
//let props = (c.GetPartitionPropertiesAsync(partitionId = "0")).Result
//printf "%d -- %d" props.BeginningSequenceNumber props.LastEnqueuedSequenceNumber
//let d = c.ReadEventsFromPartitionAsync (partitionId = "0", startingPosition = EventPosition.Earliest)
//let e = d.GetAsyncEnumerator()
//e.MoveNextAsync().AsTask().Wait()
//printf "%A" e.Current
//exit 0

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

//let j = "[{\"type\":\"UsageReported\",\"value\":{\"resourceId\":\"2b196a35-1379-4cb0-5457-4d18c28d46e6\",\"timestamp\":\"2021-12-10T14:29:10.0995601Z\",\"meterName\":\"nde\",\"quantity\":\"20\",\"properties\":{}}},{\"type\":\"UsageReported\",\"value\":{\"resourceId\":\"2b196a35-1379-4cb0-5457-4d18c28d46e6\",\"timestamp\":\"2021-12-10T14:29:10.113733Z\",\"meterName\":\"cpu\",\"quantity\":\"20\",\"properties\":{}}},{\"type\":\"UsageReported\",\"value\":{\"resourceId\":\"2b196a35-1379-4cb0-5457-4d18c28d46e6\",\"timestamp\":\"2021-12-10T14:29:10.1137354Z\",\"meterName\":\"dta\",\"quantity\":\"20\",\"properties\":{}}},{\"type\":\"UsageReported\",\"value\":{\"resourceId\":\"2b196a35-1379-4cb0-5457-4d18c28d46e6\",\"timestamp\":\"2021-12-10T14:29:10.1137357Z\",\"meterName\":\"msg\",\"quantity\":\"20\",\"properties\":{}}},{\"type\":\"UsageReported\",\"value\":{\"resourceId\":\"2b196a35-1379-4cb0-5457-4d18c28d46e6\",\"timestamp\":\"2021-12-10T14:29:10.1137359Z\",\"meterName\":\"obj\",\"quantity\":\"20\",\"properties\":{}}}]"
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
    let partitionId = "0"
    let x = 
        connections
        |> CaptureProcessor.readAllEvents CaptureProcessor.toMeteringUpdateEvent (partitionId |> PartitionID.create) CancellationToken.None 
        //|> MySeq.inspect (fun me -> $"{me.Source |> EventSource.toStr} {me.MessagePosition.SequenceNumber} {me.MessagePosition.PartitionTimestamp} " |> Some)
        |> Seq.scan MeterCollectionLogic.handleMeteringEvent MeterCollection.Empty
        |> Seq.last

    //printfn "%s" (x |> Some |> MeterCollection.toStr)
    //printfn "%s" (x |> Json.toStr 2)

    File.WriteAllText("latest.json", x |> Json.toStr 1)

    (MeterCollectionStore.storeLastState connections x CancellationToken.None).Wait()

    let x =
        File.ReadAllText("latest.json")
        |> Json.fromStr<MeterCollection>

    x.metersToBeSubmitted
    |> Seq.sortBy (fun a -> a.EffectiveStartTime.ToInstant())
    |> Seq.iter (fun a -> printfn "%s %s %s/%s %s" (a.EffectiveStartTime |> MeteringDateTime.toStr) (a.MarketplaceResourceId.ToString()) (a.PlanId.value) (a.DimensionId.value) (a.Quantity.ToString()))

| Some initialState -> 
    // let startPosition = (MessagePosition.createData partitionId 141 64576 (MeteringDateTime.fromStr "2021-12-07T18:55:38.6Z"))

    let startPosition = initialState.LastUpdate.Value

    let x = 
        connections
        |> CaptureProcessor.readEventsFromPosition CaptureProcessor.toMeteringUpdateEvent startPosition CancellationToken.None 
        // |> MySeq.inspect (fun me -> $"{me.Source |> EventSource.toStr} {me.MessagePosition.SequenceNumber} {me.MessagePosition.PartitionTimestamp} " |> Some)
        |> Seq.scan MeterCollectionLogic.handleMeteringEvent initialState
        |> Seq.last

    // printfn "%s" (x |> Some |> MeterCollection.toStr)

    printfn "%s" (x |> Json.toStr 2)

    x.metersToBeSubmitted
    |> Seq.sortBy (fun a -> a.EffectiveStartTime.ToInstant())
    |> Seq.iter (fun a -> printfn "%s %s %s/%s %s" (a.EffectiveStartTime |> MeteringDateTime.toStr) (a.MarketplaceResourceId.ToString()) (a.PlanId.value) (a.DimensionId.value) (a.Quantity.ToString()))
