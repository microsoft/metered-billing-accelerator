// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

module Metering.NUnitTests.Billing

open System
open NUnit.Framework
open Metering.BaseTypes
open Metering.BaseTypes.EventHub
open Metering.EventHub
open NodaTime

type SequenceNumberGenerator() =
    let mutable sequenceNumber = -1L
    member _.Current() = sequenceNumber
    member _.Next() =
        sequenceNumber <- sequenceNumber + 1L
        sequenceNumber

[<Test>]
let ``checkSequenceNumberGenerator``() =
    let sg1 = SequenceNumberGenerator()
    let sg2 = SequenceNumberGenerator()

    Assert.AreEqual(0L, sg1.Next())
    Assert.AreEqual(1L, sg1.Next())

    Assert.AreEqual(0L, sg2.Next())
    Assert.AreEqual(1L, sg2.Next())
    Assert.AreEqual(2L, sg2.Next())
    Assert.AreEqual(3L, sg2.Next())

type TimeGenerator(startTime: string) =
    let mutable now = MeteringDateTime.fromStr startTime
    member this.InThisHour() =
        now <- now.PlusSeconds(1)
        now |> MeteringDateTime.toStr
    member this.NextHour() =
        let oneHourLater = now.PlusHours(1)
        now <- MeteringDateTime.create (oneHourLater.Year) (oneHourLater.Month) (oneHourLater.Day) (oneHourLater.Hour) 0 1
        now |> MeteringDateTime.toStr
    member this.PreviousReportingHour() =
        let previousHour = now.Minus(Duration.FromHours(1))
        MeteringDateTime.create (previousHour.Year) (previousHour.Month) (previousHour.Day) (previousHour.Hour) 0 0 |> MeteringDateTime.toStr
    member this.Plus(duration) =
        now <- now.Plus(duration)
        now |> MeteringDateTime.toStr
    member this.Now() =
        now |> MeteringDateTime.toStr

[<Test>]
let ``CheckTimeGenerator`` () =
    let tg = TimeGenerator("2023-04-15T00:01:00Z")
    Assert.AreEqual("2023-04-15T00:01:00Z", tg.Now())
    Assert.AreEqual("2023-04-15T00:01:01Z", tg.InThisHour())
    Assert.AreEqual("2023-04-15T00:01:02Z", tg.InThisHour())
    Assert.AreEqual("2023-04-15T01:00:01Z", tg.NextHour())

[<SetUp>]
let Setup () = ()

let d = MeteringDateTime.fromStr

let runTestVectors test testcases = testcases |> List.indexed |> List.map test |> ignore

let somePlan : Plan =
    { PlanId = "PlanId" |> PlanId.create
      BillingDimensions = Map.empty }

let someManagedAppId =
    MarketplaceResourceId.fromStr "/subscriptions/.../resourceGroups/.../providers/Microsoft.Solutions/applications/myapp123"

type BillingPeriod_isInBillingPeriod_Vector = { Purchase: (RenewalInterval * string); BillingPeriodIndex: uint; Candidate: string; Expected: bool }

type MeterValue_subtractQuantityFromMeterValue_Vector = { State: SimpleMeterValue; Quantity: Quantity; Expected: SimpleMeterValue }

[<Test>]
let ``MeterValue.subtractQuantity``() =
    let lastUpdate = "2021-10-28T12:38:00" |> MeteringDateTime.fromStr
    let now =        "2021-10-28T13:38:00" |> MeteringDateTime.fromStr
    let test (idx, testcase) =
        let result = testcase.State |> SimpleMeterLogic.consume now testcase.Quantity
        Assert.AreEqual(testcase.Expected, result, sprintf "Failure test case %d" idx)

    [
        {
            // deduct without overage
            State = IncludedQuantity { RemainingQuantity = Quantity.create 30u; BillingPeriodTotal = Quantity.create 10u; LastUpdate = lastUpdate}
            Quantity = Quantity.create 13u
            Expected = IncludedQuantity { RemainingQuantity = Quantity.create 17u; BillingPeriodTotal = Quantity.create 23u; LastUpdate = now }
        }
        {
            // deplete completely
            State = IncludedQuantity { RemainingQuantity = Quantity.create 30u; BillingPeriodTotal = Quantity.create 1u; LastUpdate = lastUpdate}
            Quantity = Quantity.create 30u
            Expected = IncludedQuantity { RemainingQuantity = Quantity.Zero; BillingPeriodTotal = Quantity.create 31u; LastUpdate = now }
        }
        {
            // If there's nothing, it costs money
            State = IncludedQuantity { RemainingQuantity = Quantity.Zero; BillingPeriodTotal = Quantity.create 10u; LastUpdate = lastUpdate }
            Quantity = Quantity.create 2u
            Expected = ConsumedQuantity { CurrentHour = Quantity.create 2u; BillingPeriodTotal = Quantity.create 2u; LastUpdate = now }
        }
        {
            // Going further into the overage
            State = ConsumedQuantity { CurrentHour = Quantity.create 10u; BillingPeriodTotal = Quantity.create 200u; LastUpdate = lastUpdate }
            Quantity = Quantity.create 2u
            Expected = ConsumedQuantity { CurrentHour = Quantity.create 12u; BillingPeriodTotal = Quantity.create 202u; LastUpdate = now }
        }
        {
            // If there's infinite, it never gets depleted
            State = IncludedQuantity { RemainingQuantity = Quantity.Infinite; BillingPeriodTotal = Quantity.create 100u; LastUpdate = lastUpdate }
            Quantity = Quantity.create 200000u
            Expected = IncludedQuantity { RemainingQuantity = Quantity.Infinite; BillingPeriodTotal = Quantity.create 200100u; LastUpdate = now }
        }
    ] |> runTestVectors test

type MeterValue_topupMonthlyCredits_Vector = { Value: uint; Expected: SimpleMeterValue}

[<Test>]
let ``MeterValue.createIncluded``() =
    let now = "2021-10-28T11:38:00" |> MeteringDateTime.fromStr

    let test (idx, testcase) =
        let result = SimpleMeterLogic.createIncluded now (Quantity.create testcase.Value)
        Assert.AreEqual(testcase.Expected, result, sprintf "Failure test case %d" idx)

    [
        {
            Value = 9u
            Expected = IncludedQuantity { RemainingQuantity = Quantity.create 9u; BillingPeriodTotal = Quantity.create 0u; LastUpdate = now }
        }
    ] |> runTestVectors test

[<Test>]
let ``Meter.previousBillingIntervalCanBeClosedNewEvent``() =
    let test (idx, (prev, curEv, expected)) =
        let result =
            Meter.previousBillingIntervalCanBeClosedNewEvent
                (prev |> MeteringDateTime.fromStr)
                (curEv |> MeteringDateTime.fromStr)

        Assert.AreEqual(expected, result, sprintf "Failure test case #%d" idx)

    [
        ("2021-01-10T11:59:58", "2021-01-10T11:59:59", false) // Even though we're already 10 seconds in the new hour, the given event belongs to the previous hour, so there might be more
        ("2021-01-10T11:59:58", "2021-01-10T12:00:00", true) // The event belongs to a new period, so close it
        ("2021-01-10T12:00:00", "2021-01-11T12:00:00", true) // For whatever reason, we've been sleeping for exactly one day
    ] |> runTestVectors test

[<Test>]
let ``MarketplaceResourceId.equality``() =
    let resourceIdStr1 = "8151a707-467c-4105-df0b-44c3fca5880d"
    let resourceUriStr1 = "/subscriptions/..../resourceGroups/.../providers/Microsoft.SaaS/resources/SaaS Accelerator Test Subscription"

    Assert.AreNotEqual(
        MarketplaceResourceId.fromResourceID resourceIdStr1,
        MarketplaceResourceId.fromResourceURI resourceUriStr1)

    Assert.AreEqual(
        MarketplaceResourceId.fromResourceID resourceIdStr1,
        MarketplaceResourceId.fromStr resourceIdStr1)

    Assert.AreEqual(
        MarketplaceResourceId.fromResourceURI resourceUriStr1,
        MarketplaceResourceId.fromStr resourceUriStr1)

    let x1 = MarketplaceResourceId.fromResourceURI resourceUriStr1
    let x2 = x1.addResourceId resourceIdStr1

    Assert.IsTrue(x1.Matches(x2))
    Assert.IsTrue(x2.Matches(x1))

[<Test>]
let ``Quantity.Serialization`` () =
    let test (idx, v) =
        Assert.AreEqual(v, v |> Json.toStr 1 |> Json.fromStr<Quantity>, sprintf "Failure testc case #%d" idx)

    [
        Infinite
        Quantity.create 10u
        Quantity.create 10.1
    ] |> runTestVectors test

[<Test>]
let ``Quantity.Math`` () =
    let q : (int -> Quantity) = uint32 >> Quantity.create
    let f : (float -> Quantity) = float >> Quantity.create

    Assert.AreEqual(q 10, (q 3) + (q 7))
    Assert.AreEqual(q 7, (q 10) - (q 3))
    Assert.AreEqual(Infinite, Infinite - (q 3))
    Assert.AreEqual(Infinite, Infinite + (q 3))

    Assert.AreEqual(f 10.0, (f 3.0) + (f 7.0))
    Assert.AreEqual(f 7.0, (f 10.0) - (f 3.0))
    Assert.AreEqual(Infinite, Infinite - (f 3.0))
    Assert.AreEqual(Infinite, Infinite + (f 3.0))

    Assert.AreEqual(f 11.1, (q 3) + (f 8.1))

let partitionId = "2"

let createEvent sequenceNr timestamp (evnt: MeteringUpdateEvent) =
    let timestamp = timestamp |> MeteringDateTime.fromStr
    let messagePosition = MessagePosition.create partitionId sequenceNr timestamp
    let eventToCatchup = None

    EventHubEvent.createEventHub evnt messagePosition eventToCatchup

let createSubsc (sequenceNr: int64) (timestamp: string) (sub: SubscriptionCreationInformation) =
    sub |> MeteringUpdateEvent.SubscriptionPurchased |> createEvent sequenceNr timestamp

let createUsage (sub: MarketplaceResourceId) (dimension: string) (sequenceNr: int64) (timestamp: string) (amount: uint)  =
    { InternalUsageEvent.MarketplaceResourceId = sub
      Timestamp = timestamp |> MeteringDateTime.fromStr
      MeterName = dimension |> ApplicationInternalMeterName.create
      Quantity = amount |> Quantity.create
      Properties = None }
    |> MeteringUpdateEvent.UsageReported
    |> createEvent sequenceNr timestamp

let check (f: MeterCollection -> unit) (mc: MeterCollection) : MeterCollection =
    // check is a little helper to run a lambda 'f' on a MeterCollection, and return the MeterCollection (for piping purposes)
    f mc
    mc

[<Test>]
let ``MeterCollectionLogic.handleMeteringEvent`` () =
    let sub1 = "saas-guid-1234" |> MarketplaceResourceId.fromStr
    let sub2 = "saas-guid-5678" |> MarketplaceResourceId.fromStr

    let subCreation marketplaceResourceId start =
        {
            SubscriptionCreationInformation.Subscription = {
                Plan = {
                    PlanId = "plan123" |> PlanId.create
                    BillingDimensions =
                        [
                            (
                                "d1" |> ApplicationInternalMeterName.create,
                                {
                                    DimensionId = ("dimension1" |> DimensionId.create)
                                    IncludedQuantity = (1000u |> Quantity.create)
                                    Meter = None
                                })
                            (
                                "freestuff" |> ApplicationInternalMeterName.create,
                                {
                                    DimensionId = ("dimension2" |> DimensionId.create)
                                    IncludedQuantity = Quantity.Infinite
                                    Meter = None
                                }
                            )
                        ]
                        |> List.map (fun (name, bd) -> (name, SimpleBillingDimension bd))
                        |> Map.ofList
                }
                MarketplaceResourceId = marketplaceResourceId
                RenewalInterval = Monthly
                SubscriptionStart = start |> MeteringDateTime.fromStr
            }
        }



    let checkSub (f: Meter -> unit)  (mc: Meter) : Meter =
        f mc
        mc

    let getSimpleMeterValue (mc: MeterCollection) (marketplaceResourceId: MarketplaceResourceId) (dimensionId: string) : SimpleMeterValue =
        let dimensionId = dimensionId |> DimensionId.create

        let meter: Meter = mc |> MeterCollection.find marketplaceResourceId

        let billingDimension =
            meter.Subscription.Plan.BillingDimensions
            |> Map.toSeq
            |> Seq.find (fun (k, v) ->
                match v with
                | SimpleBillingDimension x -> x.DimensionId = dimensionId
                | _ -> false)
            |> (fun (_, v) -> v)

        match billingDimension with
        | SimpleBillingDimension x ->
            match x.Meter with
            | Some x -> x
            | _ -> failwith "Value not set"
        | _ -> failwith $"Not a {nameof SimpleBillingDimension}"

    let includes (q: Quantity) (mv: SimpleMeterValue) : unit =
        // Ensures that the given MeterValue is exactly the given quantity
        match mv with
        | IncludedQuantity iq ->
            Assert.AreEqual(q, iq.RemainingQuantity)
        | _ -> failwith "Not an IncludedQuantity"

    let overageOf (q: Quantity) (mv: SimpleMeterValue) : unit =
        match mv with
        | ConsumedQuantity cq ->
            Assert.AreEqual(q, cq.CurrentHour)
        | _ -> failwith "Not an ConsumedQuantity"

    let assertUsageReported (marketplaceResourceId: MarketplaceResourceId)  (dimension: string) (timeSlot: string) (quantity: uint) (mc: MeterCollection) : MeterCollection =
        let dimension = DimensionId.create dimension
        let timeSlot = MeteringDateTime.fromStr timeSlot
        let quantity = Quantity.create quantity

        mc
        |> MeterCollection.find marketplaceResourceId
        |> (fun x -> x.UsageToBeReported)
        |> List.filter (fun i -> i.DimensionId = dimension && i.EffectiveStartTime = timeSlot && i.MarketplaceResourceId = marketplaceResourceId && i.Quantity = quantity)
        |> List.length
        |> (fun length -> Assert.AreEqual(1, length))

        mc

    let assertOverallUsageToBeReported (marketplaceResourceId: MarketplaceResourceId) (dimension: string) (overallquantity: uint) (mc: MeterCollection) : MeterCollection =
        let dimension = DimensionId.create dimension
        let overallquantity = Quantity.create overallquantity

        let totalToBeSubmitted =
            mc.MetersToBeSubmitted()
            |> Seq.filter (fun m -> m.MarketplaceResourceId = marketplaceResourceId && m.DimensionId = dimension)
            |> Seq.sumBy (fun m -> m.Quantity.AsInt)
            |> Quantity.create

        Assert.AreEqual(overallquantity, totalToBeSubmitted)

        mc

    let sn = SequenceNumberGenerator()

    let handle a b = MeterCollectionLogic.handleMeteringEvent b a

    let newusage sub date quantity dimension =
        handle (createUsage sub dimension (sn.Next()) date quantity)

    let assertIncluded sub dimension quantity =
        check (fun m -> getSimpleMeterValue m sub dimension |> includes quantity)

    let assertOverage sub dimension (quantity: uint) =
        check (fun m -> getSimpleMeterValue m sub dimension |> overageOf (Quantity.create quantity))

    let ensureSequenceNumberHasBeenApplied =
        check (fun m ->
            match m.LastUpdate with
            | None -> Assert.Fail "missing LastUpdate"
            | Some u -> Assert.AreEqual(sn.Current(), u.SequenceNumber)
        )

    let inspectJson (header: string) (a: 'a) : 'a =
        let json = a |> Json.toStr 1
        if String.IsNullOrEmpty header
        then printfn "%s" json
        else printfn "%s: %s" header json

        printfn "---------------------------------------------------------------------------------------------------"

        a

    MeterCollection.Empty
    // after the subscription creation ...
    |> handle (createSubsc (sn.Next()) "2021-11-29T17:04:00Z" (subCreation sub1 "2021-11-29T17:00:00Z"))
    |> ensureSequenceNumberHasBeenApplied
    // ... all meters should be at their original levels
    |> check (fun m -> Assert.AreEqual(1, m.Meters.Length))
    |> check (fun m -> Assert.IsTrue(m |> MeterCollection.contains sub1))
    |> assertIncluded      sub1 "dimension1" (Quantity.create 1000u)
    |> assertIncluded      sub1 "dimension2" Quantity.Infinite
    // Up until now, there should nothing to be reported.
    |> assertOverallUsageToBeReported sub1 "dimension1" 0u
    // If we consume 999 out of 1000 included, then 1 included should remain
    |> newusage            sub1 "2021-11-29T17:04:03Z" 999u "d1"
    |> ensureSequenceNumberHasBeenApplied
    |> assertIncluded      sub1 "dimension1" (Quantity.create 1u)
    // If we consume a gazillion from the 'infinite' quantity, it should still be infinite
    |> newusage            sub1 "2021-11-29T17:05:01Z" 10000u "freestuff"
    |> ensureSequenceNumberHasBeenApplied
    |> assertIncluded      sub1 "dimension2" Quantity.Infinite
    // If we consume 2 units (from 1 included one), whe should have an overage of 1 for the current hour
    |> newusage            sub1 "2021-11-29T18:00:01Z" 2u "d1"
    |> ensureSequenceNumberHasBeenApplied
    |> assertOverage       sub1 "dimension1" 1u
    // If we consume units in the next hour, the previous usage should be wrapped for submission
    |> newusage            sub1 "2021-11-29T19:00:01Z" 2u "d1"
    |> ensureSequenceNumberHasBeenApplied
    |> assertUsageReported sub1 "dimension1" "2021-11-29T18:00:00Z" 1u
    |> assertOverallUsageToBeReported sub1 "dimension1" 1u
    |> assertOverage       sub1 "dimension1" 2u
    |> newusage            sub1 "2021-11-29T19:00:02Z" 2u "d1"
    |> ensureSequenceNumberHasBeenApplied
    |> assertOverage       sub1 "dimension1" 4u
    |> newusage            sub1 "2021-11-29T19:00:03Z" 1u "d1"
    |> ensureSequenceNumberHasBeenApplied
    |> assertOverage       sub1 "dimension1" 5u
    // After having consumed 5 additional quantities between 19:00 -- 19:59, the next event at 20:00 also closes that period.
    // Given that noone submitted / removed the previous 18:00-18:59 usage report, both are in the system
    |> newusage            sub1 "2021-11-29T20:00:03Z" 1u "d1"
    |> ensureSequenceNumberHasBeenApplied
    // |> inspectJson "What the f"
    |> assertOverage       sub1 "dimension1" 1u
    |> assertUsageReported sub1 "dimension1" "2021-11-29T18:00:00Z" 1u
    |> assertUsageReported sub1 "dimension1" "2021-11-29T19:00:00Z" 5u
    |> assertOverallUsageToBeReported sub1 "dimension1" 6u
    // let's add an additional subscription to the system
    |> handle (createSubsc (sn.Next()) "2021-11-29T19:04:00Z" (subCreation sub2 "2021-11-29T18:58:00Z"))
    |> ensureSequenceNumberHasBeenApplied
    // Now we should have 2 subsriptions
    |> check (fun m -> Assert.AreEqual(2, m.Meters.Length))
    // Submit usage to sub1 and then an hour later to sub2
    |> newusage            sub1 "2021-11-29T21:00:03Z" 1u "d1"
    |> newusage            sub2 "2021-11-29T22:00:03Z" 1u "d1"
    |> assertUsageReported sub1 "dimension1" "2021-11-29T20:00:00Z" 1u
    |> assertUsageReported sub1 "dimension1" "2021-11-29T21:00:00Z" 1u
    |> assertOverallUsageToBeReported sub1 "dimension1" 8u
    //|> Json.toStr(1)
    //|> printfn "%s"
    |> ignore

//[<Test>]
//let ``JsonRoundtrip.MarketplaceSubmissionResult`` () =
//    { MarketplaceSubmissionResult.Payload =
//        { MarketplaceResourceId = MarketplaceResourceId.ManagedApp
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
//                  MarketplaceResourceId = change.Payload.ResourceId |> MarketplaceResourceId.toStr
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
    let fiveInt = Quantity.create 5u
    let tenInt = Quantity.create 10u
    let fiveFloat = Quantity.create 5.0
    let tenFloat = Quantity.create 10.0

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
            (EventHubName.create "meteringhack-standard" "hub2",  (PartitionID.create "0"))
            blobName

    Assert.IsTrue(isRelevant
        "meteringhack-standard/hub2/p0--2021-12-08--15-17-12.avro"
        (MeteringDateTime.create 2021 12 08 15 17 12))

    Assert.IsFalse(isRelevant
        "meteringhack-standard/hub2/p0--2021-12-08--15-17-12.avro"
        (MeteringDateTime.create 2021 12 08 15 17 13))

[<Test>]
let ``CaptureProcessor.getPrefixForRelevantBlobs`` () =
    let ehContext = (EventHubName.create "meteringhack-standard" "hub2", (PartitionID.create "0"))

    Assert.AreEqual(
        "meteringhack-standard/hub2/p0--",
        CaptureProcessor.getPrefixForRelevantBlobs "{Namespace}/{EventHub}/p{PartitionId}--{Year}-{Month}-{Day}--{Hour}-{Minute}-{Second}" ehContext)

module E =
    open System.Collections.Generic
    open Azure.Messaging.EventHubs

    type MyEventData(
        eventBody: byte[],
        properties: IDictionary<string, obj>, systemProperties: IReadOnlyDictionary<string, obj>,
        sequenceNumber: int64, offset: int64, enqueuedTime: DateTimeOffset, partitionKey: string) =
        inherit EventData(new BinaryData(eventBody), properties, systemProperties, sequenceNumber, offset, enqueuedTime, partitionKey)

[<Test>]
let ``Avro.ParseEventData`` () =
    let rnd = Random()
    let bytes = Array.create 16 0uy
    rnd.NextBytes(bytes)

    let binaryGarbage = CaptureProcessor.createEventDataFromBytes "1.avro" bytes 13L 100L "0"
    let wrapped = CaptureProcessor.toMeteringUpdateEvent binaryGarbage

    ()

[<Test>]
let ``MarketplaceResourceId.Equal`` () =
    let i = MarketplaceResourceId.fromResourceID "8151a707-467c-4105-df0b-44c3fca5880d"
    let u = MarketplaceResourceId.fromResourceURI "/subscriptions/..../resourceGroups/.../providers/Microsoft.SaaS/resources/SaaS Accelerator Test Subscription"
    let ui = MarketplaceResourceId.from "/subscriptions/..../resourceGroups/.../providers/Microsoft.SaaS/resources/SaaS Accelerator Test Subscription" "8151a707-467c-4105-df0b-44c3fca5880d"

    Assert.IsTrue(ui.Matches(u))
    Assert.IsTrue(u.Matches(ui))
    Assert.IsTrue(i.Matches(ui))
    Assert.IsTrue(ui.Matches(i))
    Assert.IsFalse(u.Matches(i))

[<Test>]
let ``Meter.handleUnsuccessfulMeterSubmission.Expired``() =
    let sn = SequenceNumberGenerator()
    let tg = TimeGenerator("2023-04-15T00:01:00Z")

    let sub1 = Guid.NewGuid().ToString() |> MarketplaceResourceId.fromStr
    let bd = ApplicationInternalMeterName.create "m"

    let sub =
        {
            Plan = {
                PlanId = "p" |> PlanId.create
                BillingDimensions =
                    [
                        (
                            bd,
                            {
                                DimensionId = ("official-dimension-name" |> DimensionId.create)
                                IncludedQuantity = Quantity.Zero
                                Meter = None
                            })
                    ]
                    |> List.map (fun (name, bd) -> (name, SimpleBillingDimension bd))
                    |> Map.ofList
            }
            MarketplaceResourceId = sub1
            RenewalInterval = Monthly
            SubscriptionStart = tg.Now() |> MeteringDateTime.fromStr
        }


    let handle e mc = MeterCollectionLogic.handleMeteringEvent mc e

    let assertSubscription (sub: MarketplaceResourceId) (applicationInternalMeterName: string) (m: uint) (meterCollection: MeterCollection) : unit =
        let m = Quantity.MeteringInt m
        let meter = meterCollection |> MeterCollection.find sub
        let dim = meter.Subscription.Plan.BillingDimensions |> Map.find (ApplicationInternalMeterName.create applicationInternalMeterName)
        match dim with
        | SimpleBillingDimension sbd ->
            match sbd.Meter with
            | None -> ()
            | Some x ->
                match x with
                | IncludedQuantity iq -> ()
                | ConsumedQuantity cq -> Assert.AreEqual(m, cq.CurrentHour)
        | WaterfallBillingDimension wbd ->
            match wbd.Meter with
            | None -> ()
            | Some x -> Assert.AreEqual(m, x.Total)

    let assertUsageToBeReported (expected: bool) expectedRequest (meterCollection: MeterCollection) : MeterCollection =
        let containsElement =
            meterCollection
            |> MeterCollectionLogic.usagesToBeReported
            |> List.contains expectedRequest

        Assert.AreEqual(expected, containsElement)

        meterCollection

    let ping = PingMessage.create (partitionId |> PartitionID.create) PingReason.ProcessingStarting |> Ping

    let simulateMarketplaceResponses (nowStr: string) (mc: MeterCollection) : MeterCollection =
        let now = MeteringDateTime.fromStr nowStr
        let responseHeaders = { RequestID = Guid.NewGuid().ToString(); CorrelationID = Guid.NewGuid().ToString() }

        let responses =
            mc
            |> MeterCollectionLogic.usagesToBeReported
            |> List.map (fun usage ->
                let delta = now.Minus(usage.EffectiveStartTime)

                if Duration.Zero <= delta && delta < Duration.FromHours(24)
                then
                    { Status = { Status = SubmissionStatus.Accepted; MessageTime = now; UsageEventID = None }
                      RequestData = usage } |> Ok
                else
                    { Error = { MarketplaceError.Code = "BadArgument"; Target = Some "usageEventRequest"; Message = "One or more errors have occurred." }
                      ErrorDetails = [ { MarketplaceError.Code = "BadArgument"; Target = Some "effectiveStartTime"; Message = "The effective time provided is has expired or is in future." } ]
                      Status = { Status = SubmissionStatus.Expired; MessageTime = now; UsageEventID = None }
                      RequestData = usage } |> Expired |> Error
            )
            |> List.map (fun e -> { MarketplaceResponse.Headers = responseHeaders; Result = e } |> UsageSubmittedToAPI)
            |> List.map (fun e ->
                let sequenceNumber = sn.Next()
                printfn "%d %A" sequenceNumber e
                createEvent sequenceNumber nowStr e
                )

        // apply all marketplace responses to the system
        responses |> List.fold MeterCollectionLogic.handleMeteringEvent mc

    let metersBeforeGoingSleepALongTime =
        MeterCollection.Empty
        |> handle ( { Subscription = sub } |> MeteringUpdateEvent.SubscriptionPurchased |> createEvent (sn.Next()) (tg.InThisHour()))
        // In a new subscription, there are 3 units created in the 1st hour
        |> handle (createUsage sub1 "m" (sn.Next()) (tg.InThisHour()) 1u) |> check (assertSubscription sub1 "m" 1u)
        |> handle (createUsage sub1 "m" (sn.Next()) (tg.InThisHour()) 1u) |> check (assertSubscription sub1 "m" 2u)
        |> handle (createUsage sub1 "m" (sn.Next()) (tg.InThisHour()) 1u) |> check (assertSubscription sub1 "m" 3u)
        // Then 1 unit the next hour,
        |> handle (createUsage sub1 "m" (sn.Next()) (tg.NextHour()) 1u) |> check (assertSubscription sub1 "m" 1u)
    // Then the aggregator stops working


    let expectedRequest1 =
        { EffectiveStartTime = (tg.PreviousReportingHour() |> MeteringDateTime.fromStr)
          DimensionId = ("official-dimension-name" |> DimensionId.create)
          MarketplaceResourceId = sub.MarketplaceResourceId
          Quantity = (Quantity.create 3u)
          PlanId = sub.Plan.PlanId }
    let expectedRequest2 =
        { EffectiveStartTime = (tg.PreviousReportingHour() |> MeteringDateTime.fromStr).PlusHours(1)
          DimensionId = ("official-dimension-name" |> DimensionId.create)
          MarketplaceResourceId = sub.MarketplaceResourceId
          Quantity = (Quantity.create 1u)
          PlanId = sub.Plan.PlanId }

    let resultAfterCatchup =
        metersBeforeGoingSleepALongTime
        |> handle (createEvent (sn.Next()) (tg.Plus(Duration.FromDays(2))) ping) // When the system wakes up after 2 days
        |> assertUsageToBeReported true expectedRequest1 // Ensure we try to report 3 units the 1st hour
        |> assertUsageToBeReported true expectedRequest2 // and 1 for the following hour
        |> simulateMarketplaceResponses (tg.InThisHour()) // After rejection by marketplace
        |> assertUsageToBeReported false expectedRequest1 // both usage reports should be removed from the TODO list
        |> assertUsageToBeReported false expectedRequest2
        |> check (assertSubscription sub1 "m" 4u) // and a compentating transaction for all 4 should be for the hour in which marketplace rejected.
        |> handle (createEvent (sn.Next()) (tg.NextHour()) ping)

    let expectedRequest3 =
        { EffectiveStartTime = (tg.PreviousReportingHour() |> MeteringDateTime.fromStr)
          DimensionId = ("official-dimension-name" |> DimensionId.create)
          MarketplaceResourceId = sub.MarketplaceResourceId
          Quantity = (Quantity.create 4u)
          PlanId = sub.Plan.PlanId }

    resultAfterCatchup
    |> assertUsageToBeReported true expectedRequest3
    |> ignore
