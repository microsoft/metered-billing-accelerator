module Metering.NUnitTests.Billing

open System
open NUnit.Framework
open Metering.Types
open NodaTime

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
let Test_BillingPeriod_createFromIndex () =
    let sub = Subscription.create somePlan ManagedApp Monthly (d "2021-05-13T12:00:03") 

    Assert.AreEqual(
        (bp "2|2021-07-13T12:00:03|2021-08-12T12:00:02"),
        (BillingPeriod.createFromIndex sub 2u))

type Subscription_determineBillingPeriod_Vector = { Purchase: (RenewalInterval * string); Expected: string; Candidate: string}
        
[<Test>]
let Test_BillingPeriod_determineBillingPeriod () =
    let test (idx, testcase) =
        let (interval, purchaseDateStr) = testcase.Purchase
        let subscription = Subscription.create somePlan ManagedApp interval (d purchaseDateStr)
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
let Test_BillingPeriod_isInBillingPeriod () =
    let test (idx, testcase) =
        let (interval, purchaseDateStr) = testcase.Purchase
        let subscription = Subscription.create somePlan ManagedApp interval (d purchaseDateStr)
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
let Test_BillingPeriod_getBillingPeriodDelta () =
    let test (idx, testcase) =
        let (interval, purchaseDateStr) = testcase.Purchase
        let subscription = Subscription.create somePlan ManagedApp interval (d purchaseDateStr)
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
let Test_Logic_subtractQuantityFromMeterValue() =
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
let Test_Logic_topupMonthlyCredits() =
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
let Test_previousBillingIntervalCanBeClosedNewEvent() =
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
let Test_previousBillingIntervalCanBeClosedWakeup() =
    let test (idx, (prev, curr, (grace: float), expected)) =
        let config =
            { CurrentTimeProvider = curr |> MeteringDateTime.fromStr |> CurrentTimeProvider.AlwaysReturnSameTime
              SubmitMeteringAPIUsageEvent = SubmitMeteringAPIUsageEvent.Discard
              GracePeriod = Duration.FromHours(grace)
              ManagedResourceGroupResolver = ManagedAppResourceGroupID.retrieveDummyID "/subscriptions/deadbeef-stuff/resourceGroups/somerg"
              MeteringAPICredentials = ManagedIdentity }

        let result = prev |> MeteringDateTime.fromStr |> BillingPeriod.previousBillingIntervalCanBeClosedWakeup (config.CurrentTimeProvider(), config.GracePeriod)
                
        Assert.AreEqual(expected, result, sprintf "Failure testc case #%d" idx)

    [
        ("2021-01-10T12:00:00", "2021-01-10T14:59:59", 3.0, KeepOpen)
        ("2021-01-10T12:00:00", "2021-01-10T15:00:00", 3.0, Close)
    ] |> runTestVectors test

[<Test>]
let QuantitySerialization() =
    let test (idx, v) =
        Assert.AreEqual(v, v |> Json.toStr |> Json.fromStr<Quantity>, sprintf "Failure testc case #%d" idx)

    [
        Infinite
        Quantity.createInt 10UL
        Quantity.createFloat 10.1m
    ] |> runTestVectors test

[<Test>]
let QuantityMath() =
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
    
[<Test>]
let JsonRoundtrip_MarketplaceSubmissionResult() =
    { MarketplaceSubmissionResult.Payload =
        { ResourceId = InternalResourceId.ManagedApp
          Quantity = 2.3m
          PlanId = "plan" |> PlanId.create
          DimensionId = "dim" |> DimensionId.create
          EffectiveStartTime =  "2021-11-05T09:12:30" |> MeteringDateTime.fromStr }
      Result = "someerror" |> CommunicationsProblem |> Error
      }
    |> Json.toStr |> Json.fromStr<MarketplaceSubmissionResult>
    |> (fun x -> 
        Assert.AreEqual("plan", x.Payload.PlanId |> PlanId.value)

        match x.Result with 
        | Ok _ -> Assert.Fail "Should have been Error"
        | Error e -> 
            match e with 
            | CommunicationsProblem str -> Assert.AreEqual("someerror", str)
            | _ -> Assert.Fail $"Should have been {nameof(CommunicationsProblem)}"

        x
    )
    |> (fun change -> 
        { change with 
            Result = 
                { UsageEventId = "usageEventId 123"
                  MessageTime = "2021-11-05T09:12:30" |> MeteringDateTime.fromStr
                  Status = "Accepted"
                  ResourceId = change.Payload.ResourceId |> InternalResourceId.toStr
                  ResourceURI = "/subscriptions/..../resourceGroups/.../providers/Microsoft.SaaS/resources/SaaS Accelerator Test Subscription"
                  Quantity = change.Payload.Quantity |> Quantity.createFloat
                  DimensionId = change.Payload.DimensionId
                  EffectiveStartTime = change.Payload.EffectiveStartTime
                  PlanId = change.Payload.PlanId } |> Ok })                
    |> Json.toStr |> Json.fromStr<MarketplaceSubmissionResult>
    |> (fun x -> 
        Assert.AreEqual("plan", x.Payload.PlanId |> PlanId.value)

        match x.Result with 
        | Error _ -> Assert.Fail "Should have been Ok"
        | Ok v -> Assert.AreEqual("usageEventId 123", v.UsageEventId)
        x
    )
    |> ignore

