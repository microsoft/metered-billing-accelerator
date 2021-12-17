module Metering.NUnitTests.Billing

open System
open NUnit.Framework
open NodaTime
open Metering
open Metering.Types
open Metering.Types.EventHub
open System.IO

[<SetUp>]
let Setup () = ()

let d = MeteringDateTime.fromStr

let bp (s: string) : BillingPeriod =
    s.Split([|'|'|], 3)
    |> Array.toList
    |> List.map (fun s -> s.Trim())
    |> function
        | [indx; startVal; endVal] -> { Start = (d startVal); End = (d endVal); Index = (uint (Int64.Parse(indx))) }
        | _ -> failwith "parsing error"

let runTestVectors test testcases = testcases |> List.indexed |> List.map test |> ignore

let somePlan : Plan = 
    { PlanId = "PlanId" |> PlanId.create
      BillingDimensions = Seq.empty }

[<Test>]
let ``BillingPeriod.createFromIndex`` () =
    let sub = Subscription.create somePlan (ManagedAppIdentity |> ManagedApplication) Monthly (d "2021-05-13T12:00:03") 

    Assert.AreEqual(
        (bp "2|2021-07-13T12:00:03|2021-08-12T12:00:02"),
        (BillingPeriod.createFromIndex sub 2u))

type Subscription_determineBillingPeriod_Vector = { Purchase: (RenewalInterval * string); Expected: string; Candidate: string}
        
[<Test>]
let ``BillingPeriod.determineBillingPeriod`` () =
    let test (idx, testcase) =
        let (interval, purchaseDateStr) = testcase.Purchase
        let subscription = Subscription.create somePlan (ManagedAppIdentity |> ManagedApplication) interval (d purchaseDateStr)
        let expected : Result<BillingPeriod, BusinessError> = Ok(bp testcase.Expected)
        let compute = BillingPeriod.determineBillingPeriod subscription (d testcase.Candidate)
        Assert.AreEqual(expected, compute, sprintf "Failure test case %d expected=%A but was %A" idx expected compute);

    [
        {Purchase=(Monthly, "2021-05-13T12:00:00"); Candidate="2021-05-30T12:00:00"; Expected="0|2021-05-13T12:00:00|2021-06-12T11:59:59"}
        {Purchase=(Monthly, "2021-05-13T12:00:00"); Candidate="2021-08-01T12:00:00"; Expected="2|2021-07-13T12:00:00|2021-08-12T11:59:59"}
        // if I purchase on the 29th of Feb in a leap year, 
        // my billing renews on 28th of Feb the next year, 
        // therefore last day of the current billing period is 27th next year
        {Purchase=(Annually, "2004-02-29T12:00:00"); Candidate="2004-03-29T12:00:00"; Expected="0|2004-02-29T12:00:00|2005-02-27T11:59:59"}
        {Purchase=(Annually, "2021-05-13T12:00:00"); Candidate="2021-08-01T12:00:00"; Expected="0|2021-05-13T12:00:00|2022-05-12T11:59:59"}
        {Purchase=(Annually, "2021-05-13T12:00:00"); Candidate="2022-08-01T12:00:00"; Expected="1|2022-05-13T12:00:00|2023-05-12T11:59:59"}
    ] |> runTestVectors test

type BillingPeriod_isInBillingPeriod_Vector = { Purchase: (RenewalInterval * string); BillingPeriodIndex: uint; Candidate: string; Expected: bool}

[<Test>]
let ``BillingPeriod.isInBillingPeriod`` () =
    let test (idx, testcase) =
        let (interval, purchaseDateStr) = testcase.Purchase
        let subscription = Subscription.create somePlan (ManagedAppIdentity |> ManagedApplication) interval (d purchaseDateStr)
        let billingPeriod = testcase.BillingPeriodIndex |> BillingPeriod.createFromIndex subscription 
        let result = (d testcase.Candidate) |> BillingPeriod.isInBillingPeriod billingPeriod 
        Assert.AreEqual(testcase.Expected, result, sprintf "Failure test case %d" idx)

    [
        { Purchase=(Monthly, "2021-05-13T12:00:00"); BillingPeriodIndex=3u; Candidate="2021-08-13T12:00:00"; Expected=true}
        { Purchase=(Monthly, "2021-05-13T12:00:00"); BillingPeriodIndex=3u; Candidate="2021-08-15T12:00:00"; Expected=true}
        { Purchase=(Monthly, "2021-05-13T12:00:00"); BillingPeriodIndex=3u; Candidate="2021-09-12T11:59:59"; Expected=true}
        { Purchase=(Monthly, "2021-05-13T12:00:00"); BillingPeriodIndex=3u; Candidate="2021-09-13T12:00:00"; Expected=false}
        { Purchase=(Monthly, "2021-05-13T12:00:00"); BillingPeriodIndex=4u; Candidate="2021-09-13T12:00:00"; Expected=true}
    ] |> runTestVectors test

type BillingPeriod_getBillingPeriodDelta_Vector = { Purchase: (RenewalInterval * string); Previous: string; Current: string; Expected: BillingPeriodResult}

[<Test>]
let ``BillingPeriod.getBillingPeriodDelta`` () =
    let test (idx, testcase) =
        let (interval, purchaseDateStr) = testcase.Purchase
        let subscription = Subscription.create somePlan (ManagedAppIdentity |> ManagedApplication) interval (d purchaseDateStr)
        let result = (BillingPeriod.getBillingPeriodDelta subscription (d testcase.Previous) (d testcase.Current))
        Assert.AreEqual(testcase.Expected, result, sprintf "Failure test case %d" idx)

    [
        { Purchase=(Monthly,"2021-05-13T12:00:00"); Previous="2021-05-13T12:00:00"; Current="2021-05-13T12:00:00"; Expected=SameBillingPeriod }
        { Purchase=(Monthly,"2021-05-13T12:00:00"); Previous="2021-08-13T12:00:00"; Current="2021-08-15T12:00:00"; Expected=SameBillingPeriod }
        { Purchase=(Monthly,"2021-08-17T12:00:00"); Previous="2021-08-17T12:00:00"; Current="2021-09-12T12:00:00"; Expected=SameBillingPeriod }
        
        // When the second flips, a new BillingPeriod starts
        { Purchase=(Monthly,"2021-05-13T12:00:00"); Previous="2021-08-17T12:00:00"; Current="2021-09-13T11:59:59"; Expected=SameBillingPeriod }
        { Purchase=(Monthly,"2021-05-13T12:00:00"); Previous="2021-08-17T12:00:00"; Current="2021-09-13T12:00:00"; Expected=1u |> BillingPeriodsAgo}
        
        { Purchase=(Monthly,"2021-05-13T12:00:00"); Previous="2021-08-17T12:00:00"; Current="2021-10-13T12:00:00"; Expected=2u |> BillingPeriodsAgo }
    ] |> runTestVectors test

type MeterValue_subtractQuantityFromMeterValue_Vector = { State: MeterValue; Quantity: Quantity; Expected: MeterValue}
[<Test>]
let ``MeterValue.subtractQuantity``() =
    let created = "2021-10-28T11:38:00" |> MeteringDateTime.fromStr
    let lastUpdate = "2021-10-28T11:38:00" |> MeteringDateTime.fromStr
    let now = "2021-10-28T11:38:00" |> MeteringDateTime.fromStr
    let test (idx, testcase) = 
        let result = testcase.State |> MeterValue.subtractQuantity now testcase.Quantity 
        Assert.AreEqual(testcase.Expected, result, sprintf "Failure test case %d" idx)
    
    [ 
        {
            // if Monthly is sufficient, don't touch annual
            State = IncludedQuantity { Annually = Quantity.someInt 30UL; Monthly = Quantity.someInt 10UL; Created = created; LastUpdate = lastUpdate }
            Quantity = Quantity.createInt 8UL
            Expected = IncludedQuantity { Annually = Quantity.someInt 30UL; Monthly = Quantity.someInt 2UL; Created = created; LastUpdate = now }
        }
        {
            // if Monthly is not sufficient, also deduct from annual
            State = IncludedQuantity { Annually = Quantity.someInt 30UL; Monthly = Quantity.someInt 10UL; Created = created; LastUpdate = lastUpdate}
            Quantity = Quantity.createInt 13UL
            Expected = IncludedQuantity { Annually = Quantity.someInt 27UL; Monthly = None; Created = created; LastUpdate = now}
        }
        {
            // if both Monthly and Annual are not sufficient, it costs money
            State = IncludedQuantity { Annually = Quantity.someInt 30UL; Monthly = Quantity.someInt 10UL; Created = created; LastUpdate = lastUpdate }
            Quantity = Quantity.createInt 43UL
            Expected = ConsumedQuantity { Amount = Quantity.createInt  3UL; Created = created; LastUpdate = now }
        }
        {
            // If there's nothing, it costs money
            State = IncludedQuantity { Annually = None; Monthly = None; Created = created; LastUpdate = lastUpdate }
            Quantity = Quantity.createInt 2UL
            Expected = ConsumedQuantity { Amount = Quantity.createInt 2UL; Created = created; LastUpdate = now }
        }
        {
            // If there's nothing, it costs money
            State = ConsumedQuantity { Amount = Quantity.createInt 0UL; Created = created; LastUpdate = lastUpdate }
            Quantity = Quantity.createInt 2UL
            Expected = ConsumedQuantity { Amount = Quantity.createInt 2UL; Created = created; LastUpdate = now }
        }
        {
            // If there's nothing, it costs money
            State = ConsumedQuantity { Amount = Quantity.createInt 10UL; Created = created; LastUpdate = lastUpdate }
            Quantity = Quantity.createInt 2UL
            Expected = ConsumedQuantity { Amount = Quantity.createInt 12UL; Created = created; LastUpdate = now }
        }
    ] |> runTestVectors test

type MeterValue_topupMonthlyCredits_Vector = { Input: MeterValue; Values: (uint64 * RenewalInterval) list; Expected: MeterValue}

[<Test>]
let ``MeterValue.topupMonthlyCredits``() =
    let created = "2021-10-28T11:38:00" |> MeteringDateTime.fromStr
    let lastUpdate = "2021-10-28T11:38:00" |> MeteringDateTime.fromStr
    let now = "2021-10-28T11:38:00" |> MeteringDateTime.fromStr

    let test (idx, testcase) =
        let result = testcase.Values |> List.fold (MeterValue.topupMonthlyCredits |> (fun f a (b, c) -> a |> f now (Quantity.createInt b) c)) testcase.Input
        Assert.AreEqual(testcase.Expected, result, sprintf "Failure test case %d" idx)
    
    [
        {
            Input = IncludedQuantity { Annually = Quantity.someInt 1UL; Monthly = None; Created = created; LastUpdate = lastUpdate } 
            Values = [(9UL, Monthly)]
            Expected = IncludedQuantity { Annually = Quantity.someInt 1UL; Monthly = Quantity.someInt 9UL; Created = created; LastUpdate = now } 
        }
        {
            Input = IncludedQuantity { Annually = Quantity.someInt 1UL; Monthly = Quantity.someInt 2UL; Created = created; LastUpdate = lastUpdate } 
            Values = [(9UL, Monthly)]
            Expected = IncludedQuantity { Annually = Quantity.someInt 1UL; Monthly = Quantity.someInt 9UL; Created = created; LastUpdate = now } 
        }
        {
            Input = ConsumedQuantity { Amount = Quantity.createInt 100_000UL; Created = created; LastUpdate = lastUpdate }
            Values = [(1000UL, Monthly)]
            Expected = IncludedQuantity { Annually = None; Monthly = Quantity.someInt 1000UL; Created = created; LastUpdate = now } 
        }
        {
            Input = IncludedQuantity { Annually = Quantity.someInt 1UL; Monthly = None; Created = created; LastUpdate = lastUpdate } 
            Values = [(9UL, Annually)]
            Expected = IncludedQuantity { Annually = Quantity.someInt 9UL; Monthly = None; Created = created; LastUpdate = now } 
        }
        {
            Input = IncludedQuantity { Annually = Quantity.someInt 1UL; Monthly = Quantity.someInt 2UL; Created = created; LastUpdate = lastUpdate } 
            Values = [(9UL, Annually)]
            Expected = IncludedQuantity { Annually = Quantity.someInt 9UL ; Monthly = Quantity.someInt 2UL; Created = created; LastUpdate = now } 
        }
        {
            Input = ConsumedQuantity { Amount = Quantity.createInt 100_000UL; Created = created; LastUpdate = lastUpdate }
            Values = [(1000UL, Annually)]
            Expected = IncludedQuantity { Annually = Quantity.someInt 1000UL ; Monthly = None; Created = created; LastUpdate = now } 
        }
        {
            Input = IncludedQuantity { Annually = Quantity.someInt 1UL; Monthly = Quantity.someInt 2UL; Created = created; LastUpdate = lastUpdate } 
            Values = [
                (10_000UL, Annually)
                (500UL, Monthly)
            ]
            Expected = IncludedQuantity { Annually = Quantity.someInt 10_000UL; Monthly = Quantity.someInt 500UL; Created = created; LastUpdate = now } 
        }
    ] |> runTestVectors test

[<Test>]
let ``BillingPeriod.previousBillingIntervalCanBeClosedNewEvent``() =
    let test (idx, (prev, curEv, expected)) =
        let result : CloseBillingPeriod = 
            BillingPeriod.previousBillingIntervalCanBeClosedNewEvent
                (prev |> MeteringDateTime.fromStr)
                (curEv |> MeteringDateTime.fromStr)
        
        Assert.AreEqual(expected, result, sprintf "Failure test case #%d" idx)

    [
        ("2021-01-10T11:59:58", "2021-01-10T11:59:59", KeepOpen) // Even though we're already 10 seconds in the new hour, the given event belongs to the previous hour, so there might be more
        ("2021-01-10T11:59:58", "2021-01-10T12:00:00", Close) // The event belongs to a new period, so close it        
        ("2021-01-10T12:00:00", "2021-01-11T12:00:00", Close) // For whatever reason, we've been sleeping for exactly one day
    ] |> runTestVectors test

[<Test>]
let ``BillingPeriod.previousBillingIntervalCanBeClosedWakeup`` () =
    let test (idx, (prev, curr, (grace: float), expected)) =
        let currentTime = curr |> MeteringDateTime.fromStr
        let gracePeriod = Duration.FromHours(grace)

        let result = prev |> MeteringDateTime.fromStr |> BillingPeriod.previousBillingIntervalCanBeClosedWakeup (currentTime, gracePeriod)
                
        Assert.AreEqual(expected, result, sprintf "Failure testc case #%d" idx)

    [
        ("2021-01-10T12:00:00", "2021-01-10T14:59:59", 3.0, KeepOpen)
        ("2021-01-10T12:00:00", "2021-01-10T15:00:00", 3.0, Close)
    ] |> runTestVectors test

[<Test>]
let ``Quantity.Serialization`` () =
    let test (idx, v) =
        Assert.AreEqual(v, v |> Json.toStr 1 |> Json.fromStr<Quantity>, sprintf "Failure testc case #%d" idx)

    [
        Infinite
        Quantity.createInt 10UL
        Quantity.createFloat 10.1m
    ] |> runTestVectors test

[<Test>]
let ``Quantity.Math`` () =
    let q : (int -> Quantity) = uint64 >> Quantity.createInt
    let f : (float -> Quantity) = decimal >> Quantity.createFloat
    
    Assert.AreEqual(q 10, (q 3) + (q 7))
    Assert.AreEqual(q 7, (q 10) - (q 3))
    Assert.AreEqual(Infinite, Infinite - (q 3))
    Assert.AreEqual(Infinite, Infinite + (q 3))
    
    Assert.AreEqual(f 10.0, (f 3.0) + (f 7.0))
    Assert.AreEqual(f 7.0, (f 10.0) - (f 3.0))
    Assert.AreEqual(Infinite, Infinite - (f 3.0))
    Assert.AreEqual(Infinite, Infinite + (f 3.0))

    Assert.AreEqual(f 11.1, (q 3) + (f 8.1))
    
//[<Test>]
//let ``JsonRoundtrip.MarketplaceSubmissionResult`` () =
//    { MarketplaceSubmissionResult.Payload =
//        { ResourceId = InternalResourceId.ManagedApp
//          Quantity = 2.3m
//          PlanId = "plan" |> PlanId.create
//          DimensionId = "dim" |> DimensionId.create
//          EffectiveStartTime =  "2021-11-05T09:12:30" |> MeteringDateTime.fromStr }
//      Headers = 
//        { RequestID = Guid.NewGuid().ToString()
//          CorrelationID = Guid.NewGuid().ToString() }
//      Result = "someerror" |> CommunicationsProblem |> Error
//      }
//    |> Json.toStr 1 |> Json.fromStr<MarketplaceSubmissionResult>
//    |> (fun x -> 
//        Assert.AreEqual("plan", x.Payload.PlanId |> PlanId.value)
//
//        match x.Result with 
//        | Ok _ -> Assert.Fail "Should have been Error"
//        | Error e -> 
//            match e with 
//            | CommunicationsProblem str -> Assert.AreEqual("someerror", str)
//            | _ -> Assert.Fail $"Should have been {nameof(CommunicationsProblem)}"
//
//        x
//    )
//    |> (fun change -> 
//        { change with 
//            Result = 
//                { UsageEventId = "usageEventId 123"
//                  MessageTime = "2021-11-05T09:12:30" |> MeteringDateTime.fromStr
//                  Status = "Accepted"
//                  ResourceId = change.Payload.ResourceId |> InternalResourceId.toStr
//                  ResourceURI = "/subscriptions/..../resourceGroups/.../providers/Microsoft.SaaS/resources/SaaS Accelerator Test Subscription"
//                  Quantity = change.Payload.Quantity |> Quantity.createFloat
//                  DimensionId = change.Payload.DimensionId
//                  EffectiveStartTime = change.Payload.EffectiveStartTime
//                  PlanId = change.Payload.PlanId } |> Ok })                
//    |> Json.toStr 1 |> Json.fromStr<MarketplaceSubmissionResult>
//    |> (fun x -> 
//        Assert.AreEqual("plan", x.Payload.PlanId |> PlanId.value)
//
//        match x.Result with 
//        | Error _ -> Assert.Fail "Should have been Ok"
//        | Ok v -> Assert.AreEqual("usageEventId 123", v.UsageEventId)
//        x
//    )
//    |> ignore

[<Test>]
let ``Quantity.Comparison``() =
    let fiveInt = Quantity.createInt 5UL
    let tenInt = Quantity.createInt 10UL
    let fiveFloat = Quantity.createFloat 5M
    let tenFloat = Quantity.createFloat 10M
    
    Assert.AreEqual(fiveFloat, fiveInt)
    Assert.AreEqual(tenFloat, tenInt)

    Assert.IsTrue(fiveFloat < tenFloat)
    Assert.IsTrue(fiveInt < tenFloat)
    Assert.IsTrue(fiveFloat < tenInt)
    Assert.IsTrue(fiveInt < tenInt)

    Assert.IsTrue(tenFloat > fiveFloat)
    Assert.IsTrue(tenFloat > fiveInt)
    Assert.IsTrue(tenInt > fiveFloat)
    Assert.IsTrue(tenInt > fiveInt)

    Assert.IsTrue(fiveFloat <= tenFloat)
    Assert.IsTrue(fiveInt <= tenFloat)
    Assert.IsTrue(fiveFloat <= tenInt)
    Assert.IsTrue(fiveInt <= tenInt)

    Assert.IsTrue(tenFloat >= fiveFloat)
    Assert.IsTrue(tenFloat >= fiveInt)
    Assert.IsTrue(tenInt >= fiveFloat)
    Assert.IsTrue(tenInt >= fiveInt)

[<Test>]
let ``CaptureProcessor.isRelevantBlob`` () =
    let isRelevant blobName =
        CaptureProcessor.isRelevantBlob 
            "{Namespace}/{EventHub}/p{PartitionId}--{Year}-{Month}-{Day}--{Hour}-{Minute}-{Second}"
            (EventHubName.create "meteringhack-standard" "hub2", "0")
            blobName

    Assert.IsTrue(isRelevant 
        "meteringhack-standard/hub2/p0--2021-12-08--15-17-12.avro" 
        (MeteringDateTime.create 2021 12 08 15 17 12))

    Assert.IsFalse(isRelevant 
        "meteringhack-standard/hub2/p0--2021-12-08--15-17-12.avro" 
        (MeteringDateTime.create 2021 12 08 15 17 13))
    
[<Test>]
let ``CaptureProcessor.getPrefixForRelevantBlobs`` () =
    let ehContext = (EventHubName.create "meteringhack-standard" "hub2", "0")

    Assert.AreEqual(
        "meteringhack-standard/hub2/p0--", 
        CaptureProcessor.getPrefixForRelevantBlobs "{Namespace}/{EventHub}/p{PartitionId}--{Year}-{Month}-{Day}--{Hour}-{Minute}-{Second}" ehContext)


let private roundTrip<'T> (filename: string) =
    let json =
        $"data/{filename}"
        |> File.ReadAllText
    
    let t1 = json |> Json.fromStr<'T>
    
    let t2 = 
        t1
        |> Json.toStr 0
        |> Json.fromStr<'T>

    Assert.AreEqual(t1, t2, message = $"Inputfile: data/{filename}")

[<Test>]
let RoundTripMarketplaceStructures () =
    roundTrip<MarketplaceRequest> "MarketplaceRequest.json"
    roundTrip<MarketplaceRequest> "MarketplaceRequest.json"
    roundTrip<MarketplaceSuccessResponse> "MarketplaceSuccessResponse.json"
    roundTrip<MarketplaceErrorDuplicate> "MarketplaceErrorDuplicate.json"
    roundTrip<MarketplaceGenericError> "MarketplaceGenericError.json"
    roundTrip<MarketplaceSubmissionError> "MarketplaceErrorDuplicate.json"
    roundTrip<MarketplaceSubmissionError> "MarketplaceGenericError.json"
    roundTrip<MarketplaceBatchRequest> "MarketplaceBatchRequest.json"
    roundTrip<MarketplaceBatchResponseDTO> "MarketplaceBatchResponseDTO.json"

    [ "MarketplaceSuccessResponse.json"
      "MarketplaceErrorDuplicate.json"
      "MarketplaceGenericError.json" ]
    |> List.iter roundTrip<MarketplaceSubmissionResult>

module E =
    open System.Collections.Generic
    open Azure.Messaging.EventHubs
    
    type MyEventData(
        eventBody: byte[], 
        properties: IDictionary<string, obj>, systemProperties: IReadOnlyDictionary<string, obj>, 
        sequenceNumber: int64, offset: int64, enqueuedTime: DateTimeOffset, partitionKey: string) =                 
        inherit EventData(new BinaryData(eventBody), properties, systemProperties, sequenceNumber, offset, enqueuedTime, partitionKey)

[<Test>]
let ParseEventData () =
    
    let rnd = Random()
    let bytes = Array.create 16 0uy
    rnd.NextBytes(bytes)
    
    let binaryGarbage = EventDataDummy.create "1.avro" bytes 13L 100L "0"
    let wrapped = EventHubObservableClient.toMeteringUpdateEvent binaryGarbage
    
    ()


    
