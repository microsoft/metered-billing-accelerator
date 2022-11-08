// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

module Metering.NUnitTests.WaterfallUnitTests

open NUnit.Framework
open Metering.BaseTypes
open Metering.BaseTypes.WaterfallTypes

open WaterfallMeter

[<SetUp>]
let Setup () = ()

let GigaByte (x: uint) = Quantity.create x
let TeraByte x = GigaByte (1000u * x) // yeah, I know, 1024, but this helps readability

let assertTotal expected meter =
    Assert.AreEqual(expected, meter.Total)
    meter

let assertConsumption expected (meter: WaterfallMeter) =
    Assert.AreEqual(expected, meter.Consumption)
    meter

let tier0_0_99 = "First 100GB / Month" |> DimensionId.create
let tier1_100_10099 = "Next 10TB / Month" |> DimensionId.create
let tier2_10100_50099 = "Next 40TB / Month" |> DimensionId.create
let tier3_50100_150099 = "Next 100TB / Month" |> DimensionId.create
let tier4_150100_500099= "Next 350TB / Month" |> DimensionId.create
let tier5_500100_and_more = "Overage over 500TB" |> DimensionId.create

[<Test>]
let CreateMeterWithIncludedQuantities () =
    let meter =
        [
            { Begin = GigaByte 100u; Name = tier1_100_10099 }
            { Begin = TeraByte  10u; Name = tier2_10100_50099 }
            { Begin = TeraByte  40u; Name = tier3_50100_150099 }
            { Begin = TeraByte 100u; Name = tier4_150100_500099 }
            { Begin = TeraByte 350u; Name = tier5_500100_and_more }
        ]
        |> WaterfallMeter.create

    let expected = 
        { Total = Quantity.Zero; Consumption = Map.empty 
          Model = [
            FreeIncluded               (GigaByte     100u)
            Range {   LowerIncluding = (GigaByte     100u); UpperExcluding = (GigaByte  10_100u); DimensionId = tier1_100_10099 }
            Range {   LowerIncluding = (GigaByte  10_100u); UpperExcluding = (GigaByte  50_100u); DimensionId = tier2_10100_50099 }
            Range {   LowerIncluding = (GigaByte  50_100u); UpperExcluding = (GigaByte 150_100u); DimensionId = tier3_50100_150099 }
            Range {   LowerIncluding = (GigaByte 150_100u); UpperExcluding = (GigaByte 500_100u); DimensionId = tier4_150100_500099 }
            Overage { LowerIncluding = (GigaByte 500_100u);                                       DimensionId = tier5_500100_and_more }
          ] }

    Assert.AreEqual(expected, meter)

    let submitDataToMeteringAndEmptyConsumption (x: WaterfallMeter) = { x with Consumption = Map.empty }
    
    meter
    |> consume (GigaByte     50u) |> assertTotal (GigaByte      50u) |> assertConsumption Map.empty
    |> consume (GigaByte     49u) |> assertTotal (GigaByte      99u) |> assertConsumption Map.empty
    |> consume (GigaByte      2u) |> assertTotal (GigaByte     101u) |> assertConsumption (Map.ofSeq [(tier1_100_10099, (GigaByte      1u))])
    |> consume (GigaByte      1u) |> assertTotal (GigaByte     102u) |> assertConsumption (Map.ofSeq [(tier1_100_10099, (GigaByte      2u))])
    |> consume (GigaByte  9_997u) |> assertTotal (GigaByte  10_099u) |> assertConsumption (Map.ofSeq [(tier1_100_10099, (GigaByte  9_999u))])
    |> consume (GigaByte      1u) |> assertTotal (GigaByte  10_100u) |> assertConsumption (Map.ofSeq [(tier1_100_10099, (GigaByte 10_000u))])
    |> consume (GigaByte      1u) |> assertTotal (GigaByte  10_101u) |> assertConsumption (Map.ofSeq [(tier1_100_10099, (GigaByte 10_000u)); (tier2_10100_50099, (GigaByte 1u))])
    |> consume (GigaByte      2u) |> assertTotal (GigaByte  10_103u) |> assertConsumption (Map.ofSeq [(tier1_100_10099, (GigaByte 10_000u)); (tier2_10100_50099, (GigaByte 3u))])
    |> consume (GigaByte 39_996u) |> assertTotal (GigaByte  50_099u) |> assertConsumption (Map.ofSeq [(tier1_100_10099, (GigaByte 10_000u)); (tier2_10100_50099, (GigaByte 39_999u))])
    |> consume (GigaByte      1u) |> assertTotal (GigaByte  50_100u) |> assertConsumption (Map.ofSeq [(tier1_100_10099, (GigaByte 10_000u)); (tier2_10100_50099, (GigaByte 40_000u))])
    |> consume (GigaByte      1u) |> assertTotal (GigaByte  50_101u) |> assertConsumption (Map.ofSeq [(tier1_100_10099, (GigaByte 10_000u)); (tier2_10100_50099, (GigaByte 40_000u)); (tier3_50100_150099, (GigaByte 1u))])
    |> consume (GigaByte 99_998u) |> assertTotal (GigaByte 150_099u) |> assertConsumption (Map.ofSeq [(tier1_100_10099, (GigaByte 10_000u)); (tier2_10100_50099, (GigaByte 40_000u)); (tier3_50100_150099, (GigaByte 99_999u))])
    |> consume (GigaByte      2u) |> assertTotal (GigaByte 150_101u) |> assertConsumption (Map.ofSeq [(tier1_100_10099, (GigaByte 10_000u)); (tier2_10100_50099, (GigaByte 40_000u)); (tier3_50100_150099, (GigaByte 100_000u)); (tier4_150100_500099, (GigaByte 1u))])
    |> submitDataToMeteringAndEmptyConsumption // simulate we submitted the data. 1 unit from tier4 is submitted here already, that's why the remaining quantity for tier4 is 349_999UL
    |> consume (GigaByte       2u) |> assertTotal (GigaByte 150_103u) |> assertConsumption (Map.ofSeq [(tier4_150100_500099, (GigaByte 2u))])
    |> consume (GigaByte 350_000u) |> assertTotal (GigaByte 500_103u) |> assertConsumption (Map.ofSeq [(tier4_150100_500099, (GigaByte 349_999u)); (tier5_500100_and_more, (GigaByte 3u))])
    |> ignore

[<Test>]
let CreateMeterWithOutIncludedQuantities () =
    let meter =
        [
            { Begin = GigaByte   0u; Name = tier0_0_99 }
            { Begin = GigaByte 100u; Name = tier1_100_10099 }
            { Begin = TeraByte  10u; Name = tier2_10100_50099 }
            { Begin = TeraByte  40u; Name = tier3_50100_150099 }
            { Begin = TeraByte 100u; Name = tier4_150100_500099 }
            { Begin = TeraByte 350u; Name = tier5_500100_and_more }
        ]
        |> create

    let expected = 
        { Total = Quantity.Zero; Consumption = Map.empty 
          Model = [
            Range {   LowerIncluding = (GigaByte       0u); UpperExcluding = (GigaByte     100u); DimensionId = tier0_0_99 }
            Range {   LowerIncluding = (GigaByte     100u); UpperExcluding = (GigaByte  10_100u); DimensionId = tier1_100_10099 }
            Range {   LowerIncluding = (GigaByte  10_100u); UpperExcluding = (GigaByte  50_100u); DimensionId = tier2_10100_50099 }
            Range {   LowerIncluding = (GigaByte  50_100u); UpperExcluding = (GigaByte 150_100u); DimensionId = tier3_50100_150099 }
            Range {   LowerIncluding = (GigaByte 150_100u); UpperExcluding = (GigaByte 500_100u); DimensionId = tier4_150100_500099 }
            Overage { LowerIncluding = (GigaByte 500_100u);                                       DimensionId = tier5_500100_and_more }
          ] }

    Assert.AreEqual(expected, meter)