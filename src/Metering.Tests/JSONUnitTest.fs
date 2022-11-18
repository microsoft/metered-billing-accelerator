// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

module Metering.NUnitTests.JSON

open System.IO
open NUnit.Framework
open Metering.BaseTypes
open Metering.BaseTypes.WaterfallTypes

[<SetUp>]
let Setup () = ()

let private roundTrip<'T> (filename: string) =
    let json =
        $"data/{filename}"
        |> File.ReadAllText
    
    let t1 = json |> Json.fromStr<'T>

    printfn "--------------------------- %s \n%A" filename t1
    
    let t2 = 
        t1
        |> Json.toStr 0
        |> Json.fromStr<'T>

    Assert.AreEqual(t1, t2, message = $"Inputfile: data/{filename}")

[<Test>]
let ``Json.MarketplaceRequest`` () = roundTrip<MarketplaceRequest> "MarketplaceRequest.json"

[<Test>]
let ``Json.MarketplaceSuccessResponseMarketplaceSuccessResponse`` () = roundTrip<MarketplaceSuccessResponse> "MarketplaceSuccessResponse.json"

[<Test>]
let ``Json.MarketplaceSubmissionResultMarketplaceSuccessResponse`` () = roundTrip<MarketplaceSubmissionResult> "MarketplaceSuccessResponse.json"

[<Test>]
let ``Json.MarketplaceErrorDuplicateMarketplaceErrorDuplicate`` () = roundTrip<MarketplaceErrorDuplicate> "MarketplaceErrorDuplicate.json"

[<Test>]
let ``Json.MarketplaceGenericErrorMarketplaceGenericError`` () = roundTrip<MarketplaceGenericError> "MarketplaceGenericError.json"

[<Test>]
let ``Json.MarketplaceSubmissionErrorMarketplaceErrorDuplicate`` () = roundTrip<MarketplaceSubmissionError> "MarketplaceErrorDuplicate.json"

[<Test>]
let ``Json.MarketplaceSubmissionResultMarketplaceErrorDuplicate`` () = roundTrip<MarketplaceSubmissionResult> "MarketplaceErrorDuplicate.json"

[<Test>]
let ``Json.MarketplaceSubmissionErrorMarketplaceGenericError`` () = roundTrip<MarketplaceSubmissionError> "MarketplaceGenericError.json"

[<Test>]
let ``Json.MarketplaceSubmissionResultMarketplaceGenericError`` () = roundTrip<MarketplaceSubmissionResult> "MarketplaceGenericError.json"

[<Test>]
let ``Json.BillingDimensionSimple`` () = roundTrip<BillingDimension> "simpleDimension.json"

[<Test>]
let ``Json.MarketplaceBatchRequest`` () = roundTrip<MarketplaceBatchRequest> "MarketplaceBatchRequest.json"

[<Test>]
let ``Json.MarketplaceBatchResponseDTO`` () = roundTrip<MarketplaceBatchResponseDTO> "MarketplaceBatchResponseDTO.json"

[<Test>]
let ``Json.BillingDimensionsDTO`` () = roundTrip<BillingDimensions> "BillingDimensions.json"

[<Test>]
let ``Json.SimpleConsumptionBillingDimension`` () = roundTrip<SimpleConsumptionBillingDimension list> "SimpleConsumptionBillingDimension.json"

[<Test>]
let ``Json.SimpleMeterValueList`` () = roundTrip<SimpleMeterValue list> "SimpleMeterValue.json"

[<Test>]
let ``Json.WaterfallBillingDimensionList`` () = roundTrip<WaterfallBillingDimension list> "WaterfallBillingDimensionList.json"

[<Test>]
let ``Json.MeteringUpdateEvent`` () = 
    [
        "usageReported"
        "usageReportedOnlyResourceUri"
        "usageReportedOnlyResourceId"
        "subscriptionDeleted1"
        "subscriptionDeleted2"
        "subscriptionDeleted3"
        "subscriptionPurchased"
    ]
    |> List.iter (fun name -> roundTrip<MeteringUpdateEvent> $"messages/{name}.json")

[<Test>]
let ``Json.plan`` () = roundTrip<Plan> "plan.json"

[<Test>]
let ``Json.state`` () = roundTrip<MeterCollection> "state.json"

[<Test>]
let ``Json.ParsePlan`` () =
    let p =
        """
        {
          "planId": "the_plan",
          "billingDimensions": {
            "literal":  { "type": "simple", "dimension": "literal", "included": 2 },
            "quoted":   { "type": "simple", "dimension": "quoted", "included": "1000000" },
            "infinite": { "type": "simple", "dimension": "cpucharge", "included": "Infinite" }
          }
        }
        """
        |> Json.fromStr<Plan>
    Assert.AreEqual("the_plan", p.PlanId.value)

    let check (appInternalName: string) (expected: Quantity) =
        let actual = 
            p.BillingDimensions.value
            |> Map.find (appInternalName |> ApplicationInternalMeterName.create)
            |> function
                | SimpleConsumptionBillingDimension dim -> dim.IncludedQuantity
                | _ -> failwith $"Should have been a {nameof(SimpleConsumptionBillingDimension)}"

        Assert.AreEqual(expected, actual)
    
    check "infinite" Quantity.Infinite
    check "literal" (Quantity.create 2u)
    check "quoted" (Quantity.create 1000000u)

