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
    let sub = Subscription.create Monthly (LocalDate(2021, 5, 13))
          
    Assert.AreEqual(
        (bp "2|2021-07-13|2021-08-12"),
        (BillingPeriod.createFromIndex sub 2u))

[<Test>]
let Test_Subscription_determineBillingPeriod () =
    let subscriptionStart = d "2021-05-13"
    let monthlySub = Subscription.create Monthly subscriptionStart
    let yearlySub = Subscription.create Yearly subscriptionStart

    let vectors : (Subscription * Result<BillingPeriod, BusinessError> * LocalDate) list= [
        (monthlySub, Error(DayBeforeSubscription), (LocalDate(2021, 1, 1)))
        (monthlySub, Ok(bp "0|2021-05-13|2021-06-12"), (d "2021-05-30"))
        (monthlySub, Ok(bp "2|2021-07-13|2021-08-12"), (d "2021-08-01"))
        (yearlySub,  Ok(bp "0|2021-05-13|2022-05-12"), (d "2021-08-01"))
        (yearlySub,  Ok(bp "1|2022-05-13|2023-05-12"), (d "2022-08-01"))
    ]

    for (sub, expected, input) in vectors do
        Assert.AreEqual(expected, Subscription.determineBillingPeriod sub input);
