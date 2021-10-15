module Metering.NUnitTests.Billing

open System
open NUnit.Framework
open NodaTime
open Metering
open Metering.Types
open Metering.BillingPeriod

[<SetUp>]
let Setup () = ()

let d (s: string) : LocalDate =
    LocalDate.FromDateTime(DateTime.ParseExact(s, "yyyy-MM-dd", null))

let bp (s: string) : BillingPeriod =
    s.Split([|'|'|], 3)
    |> Array.toList
    |> List.map (fun s -> s.Trim())
    |> function
        | [idx; start; ending] -> 
            { 
                FirstDay = (d start)
                LastDay = (d ending)
                Index = (uint (Int64.Parse(idx)))
            }
        | _ -> failwith "parsing error"

let runTestVectors test testcases = testcases |> List.indexed |> List.map test |> ignore

[<Test>]
let Test_BillingPeriod_createFromIndex () =
    let sub = Subscription.create "planId" Monthly (d "2021-05-13") 

    Assert.AreEqual(
        (bp "2|2021-07-13|2021-08-12"),
        (BillingPeriod.createFromIndex sub 2u))

type Subscription_determineBillingPeriod_Vector = { T: RenewalInterval; Purchase: string; Interval: string; Candidate: string}
        
[<Test>]
let Test_BillingPeriod_determineBillingPeriod () =
    let test (idx, testcase) =
        let sub = Subscription.create "planId" testcase.T (d testcase.Purchase)
        let expected : Result<BillingPeriod, BusinessError> = Ok(bp testcase.Interval)
        let compute = BillingPeriod.determineBillingPeriod sub (d testcase.Candidate)
        Assert.AreEqual(expected, compute, sprintf "Failure test case %d" idx);

    [
        {T=Monthly; Purchase="2021-05-13"; Interval="0|2021-05-13|2021-06-12"; Candidate="2021-05-30"}
        {T=Monthly; Purchase="2021-05-13"; Interval="2|2021-07-13|2021-08-12"; Candidate="2021-08-01"}
        // if I purchase on the 29th of Feb in a leap year, 
        // my billing renews on 28th of Feb the next year, 
        // therefore last day of the current billing period is 27th next year
        {T=Annually; Purchase="2004-02-29"; Interval="0|2004-02-29|2005-02-27"; Candidate="2004-03-29"}
        {T=Annually; Purchase="2021-05-13"; Interval="0|2021-05-13|2022-05-12"; Candidate="2021-08-01"}
        {T=Annually; Purchase="2021-05-13"; Interval="1|2022-05-13|2023-05-12"; Candidate="2022-08-01"}
    ] |> runTestVectors test

type BillingPeriod_isInBillingPeriod_Vector = { T: RenewalInterval; Purchase: string; BillingPeriodIndex: uint; Candidate: string; Expected: bool}

[<Test>]
let Test_BillingPeriod_isInBillingPeriod () =
    let test (idx, testcase) =
        let subscription = Subscription.create "planId" testcase.T (d testcase.Purchase) 
        let billingPeriod = testcase.BillingPeriodIndex |> BillingPeriod.createFromIndex subscription 
        let result = (d testcase.Candidate) |> BillingPeriod.isInBillingPeriod billingPeriod 
        Assert.AreEqual(testcase.Expected, result, sprintf "Failure test case %d" idx)

    [
        { T=Monthly; Purchase="2021-05-13"; BillingPeriodIndex=3u; Candidate="2021-08-13"; Expected=true}
        { T=Monthly; Purchase="2021-05-13"; BillingPeriodIndex=3u; Candidate="2021-08-15"; Expected=true}
        { T=Monthly; Purchase="2021-05-13"; BillingPeriodIndex=3u; Candidate="2021-09-12"; Expected=true}
        { T=Monthly; Purchase="2021-05-13"; BillingPeriodIndex=3u; Candidate="2021-09-13"; Expected=false}
        { T=Monthly; Purchase="2021-05-13"; BillingPeriodIndex=4u; Candidate="2021-09-13"; Expected=true}
    ] |> runTestVectors test

type BillingPeriod_getBillingPeriodDelta_Vector = { T: RenewalInterval; Purchase: string; Previous: string; Current: string; Expected: BillingPeriodResult}

[<Test>]
let Test_BillingPeriod_getBillingPeriodDelta () =
    let test (idx, testcase) =
        let subscription = Subscription.create "planId" testcase.T (testcase.Purchase |> d)
        let result = (BillingPeriod.getBillingPeriodDelta subscription (d testcase.Previous) (d testcase.Current))
        Assert.AreEqual(testcase.Expected, result, sprintf "Failure test case %d" idx)

    [
        { T = Monthly; Purchase = "2021-05-13"; Previous = "2021-05-13"; Current = "2021-05-13"; Expected = SameBillingPeriod}
        { T = Monthly; Purchase = "2021-05-13"; Previous = "2021-08-13"; Current = "2021-08-15"; Expected = SameBillingPeriod}
        { T = Monthly; Purchase = "2021-08-17"; Previous = "2021-08-17"; Current = "2021-09-12"; Expected = SameBillingPeriod}
        { T = Monthly; Purchase = "2021-05-13"; Previous = "2021-08-17"; Current = "2021-09-13"; Expected = (BillingPeriodDistance 1u)}
        { T = Monthly; Purchase = "2021-05-13"; Previous = "2021-08-17"; Current = "2021-10-13"; Expected = (BillingPeriodDistance 2u)}
    ] |> runTestVectors test

type MeterValue_deductVector = { State: MeterValue; Quantity: Quantity; Expected: MeterValue}
[<Test>]
let Test_MeterValue_deduct() =
    let test (idx, testcase) = 
        let result = MeterValue.deduct testcase.State testcase.Quantity
        Assert.AreEqual(testcase.Expected, result, sprintf "Failure test case %d" idx)
    
    [ 
        {
            // if Monthly is sufficient, don't touch annual
            State = IncludedQuantity { Annually = Some 30UL; Monthly = Some 10UL }
            Quantity = 8UL
            Expected = IncludedQuantity { Annually = Some 30UL; Monthly = Some 2UL }
        }
        {
            // if Monthly is not sufficient, also deduct from annual
            State = IncludedQuantity { Annually = Some 30UL; Monthly = Some 10UL}
            Quantity = 13UL
            Expected = IncludedQuantity { Annually = Some 27UL; Monthly = None}
        }
        {
            // if both Monthly and Annual are not sufficient, it costs money
            State = IncludedQuantity { Annually = Some 30UL; Monthly = Some 10UL }
            Quantity = 43UL
            Expected = ConsumedQuantity { Amount = 3UL }
        }
        {
            // If there's nothing, it costs money
            State = IncludedQuantity { Annually = None; Monthly = None}
            Quantity = 2UL
            Expected = ConsumedQuantity { Amount = 2UL }
        }
        {
            // If there's nothing, it costs money
            State = ConsumedQuantity { Amount = 0UL }
            Quantity = 2UL
            Expected = ConsumedQuantity { Amount = 2UL }
        }
        {
            // If there's nothing, it costs money
            State = ConsumedQuantity { Amount = 10UL }
            Quantity = 2UL
            Expected = ConsumedQuantity { Amount = 12UL }
        }
    ] |> runTestVectors test

type MeterValue_topupMonthlyCredits_Vector = { Input: MeterValue; Values: (Quantity * RenewalInterval) list; Expected: MeterValue}

[<Test>]
let Test_MeterValue_topupMonthlyCredits() =    
    let test (idx, testcase) =
        let result = testcase.Values |> List.fold MeterValue.topupMonthlyCredits testcase.Input
        Assert.AreEqual(testcase.Expected, result, sprintf "Failure test case %d" idx)
    
    [
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = None } 
            Values = [(9UL, Monthly)]
            Expected = IncludedQuantity { Annually = Some 1UL; Monthly = Some 9UL } 
        }
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = Some 2UL } 
            Values = [(9UL, Monthly)]
            Expected = IncludedQuantity { Annually = Some 1UL; Monthly = Some 9UL } 
        }
        {
            Input = ConsumedQuantity { Amount = 100_000UL }
            Values = [(1000UL, Monthly)]
            Expected = IncludedQuantity { Annually = None; Monthly = Some 1000UL } 
        }
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = None } 
            Values = [(9UL, Annually)]
            Expected = IncludedQuantity { Annually = Some 9UL; Monthly = None } 
        }
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = Some 2UL } 
            Values = [(9UL, Annually)]
            Expected = IncludedQuantity { Annually = Some 9UL ; Monthly = Some 2UL } 
        }
        {
            Input = ConsumedQuantity { Amount = 100_000UL }
            Values = [(1000UL, Annually)]
            Expected = IncludedQuantity { Annually = Some 1000UL ; Monthly = None } 
        }
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = Some 2UL } 
            Values = [
                (10_000UL, Annually)
                (500UL, Monthly)
            ]
            Expected = IncludedQuantity { Annually = Some 10_000UL; Monthly = Some 500UL } 
        }
    ] |> runTestVectors test
