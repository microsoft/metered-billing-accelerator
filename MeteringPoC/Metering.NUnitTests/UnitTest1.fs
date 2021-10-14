module Metering.NUnitTests

open NUnit.Framework
open Metering.Types
open Metering.Billing
open NodaTime

[<SetUp>]
let Setup () =
    ()

[<Test>]
let Test_BillingPeriod_createFromIndex () =
    let sub = Subscription.create Monthly (LocalDate(2021, 5, 13))
          
    Assert.AreEqual(
        { FirstDay = (LocalDate(2021, 7, 13)) ; LastDay = (LocalDate(2021, 8, 12)) ; Index = 2u },
        (BillingPeriod.createFromIndex sub 2u) )
