module Metering.NUnitTests.Billing

open System
open NUnit.Framework
open NodaTime
open Metering.Billing


[<SetUp>]
let Setup () =
    ()

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


[<Test>]
let Test_BillingPeriod_createFromIndex () =
    let sub = Subscription.create Monthly (d "2021-05-13")
          
    Assert.AreEqual(
        (bp "2|2021-07-13|2021-08-12"),
        (BillingPeriod.createFromIndex sub 2u))

[<Test>]
let Test_Subscription_determineBillingPeriod () =
    let vectors = [
        (Monthly, "2021-05-13", "0|2021-05-13|2021-06-12", "2021-05-30")
        (Monthly, "2021-05-13", "2|2021-07-13|2021-08-12", "2021-08-01")
        (Yearly,  "2021-05-13", "0|2021-05-13|2022-05-12", "2021-08-01")
        (Yearly,  "2021-05-13", "1|2022-05-13|2023-05-12", "2022-08-01")
    ]

    for (interval, startStr, billingPeriodStr, inputStr) in vectors do
        let startDate = d startStr
        let dateToCheck = d inputStr
        let sub = Subscription.create interval startDate
        let expected : Result<BillingPeriod, BusinessError> = Ok(bp billingPeriodStr)
        let compute = Subscription.determineBillingPeriod sub dateToCheck
        Assert.AreEqual(expected, compute);

[<Test>]
let Test_BillingPeriod_isInBillingPeriod () =
    let sub = Subscription.create Monthly (d "2021-05-13")
    let bp = BillingPeriod.createFromIndex sub 
    Assert.IsTrue(BillingPeriod.isInBillingPeriod (bp 3u) (d "2021-08-13"))
    Assert.IsTrue(BillingPeriod.isInBillingPeriod (bp 3u) (d "2021-08-15"))
    Assert.IsTrue(BillingPeriod.isInBillingPeriod (bp 3u) (d "2021-09-12"))
    Assert.IsFalse(BillingPeriod.isInBillingPeriod (bp 3u) (d "2021-09-13"))
    Assert.IsTrue(BillingPeriod.isInBillingPeriod (bp 4u) (d "2021-09-13"))
    