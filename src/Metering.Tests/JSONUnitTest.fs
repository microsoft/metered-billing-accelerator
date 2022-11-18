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
let ``InternalDataStructures.MeteringDateTime list`` () = roundTrip<MeteringDateTime list> "InternalDataStructures/MeteringDateTimeList.json"

[<Test>]
let ``InternalDataStructures.MarketplaceResourceIdList list`` () = roundTrip<MarketplaceResourceId list> "InternalDataStructures/MarketplaceResourceIdList.json"

[<Test>]
let ``InternalDataStructures.BillingDimension`` () = roundTrip<BillingDimension> "InternalDataStructures/simpleDimension.json"

[<Test>]
let ``InternalDataStructures.BillingDimensions`` () = roundTrip<BillingDimensions> "InternalDataStructures/BillingDimensions.json"

[<Test>]
let ``InternalDataStructures.SimpleConsumptionBillingDimension list`` () = roundTrip<SimpleConsumptionBillingDimension list> "InternalDataStructures/SimpleConsumptionBillingDimension.json"

[<Test>]
let ``InternalDataStructures.SimpleMeterValue list`` () = roundTrip<SimpleMeterValue list> "InternalDataStructures/SimpleMeterValue.json"

[<Test>]
let ``InternalDataStructures.WaterfallBillingDimension List`` () = roundTrip<WaterfallBillingDimension list> "InternalDataStructures/WaterfallBillingDimensionList.json"

[<Test>]
let ``InternalDataStructures.Plan`` () = roundTrip<Plan> "InternalDataStructures/plan.json"

[<Test>]
let ``InternalDataStructures.MeterCollection`` () = roundTrip<MeterCollection> "InternalDataStructures/MeterCollection.json"

[<Test>]
let ``InternalDataStructures.ParsePlan`` () =
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

[<Test>]
let ``Marketplace.Request`` () = roundTrip<MarketplaceRequest> "MarketplaceMessages/MarketplaceRequest.json"

[<Test>]
let ``Marketplace.SuccessResponseMarketplaceSuccessResponse`` () = roundTrip<MarketplaceSuccessResponse> "MarketplaceMessages/MarketplaceSuccessResponse.json"

[<Test>]
let ``Marketplace.SubmissionResultMarketplaceSuccessResponse`` () = roundTrip<MarketplaceSubmissionResult> "MarketplaceMessages/MarketplaceSuccessResponse.json"

[<Test>]
let ``Marketplace.ErrorDuplicateMarketplaceErrorDuplicate`` () = roundTrip<MarketplaceErrorDuplicate> "MarketplaceMessages/MarketplaceErrorDuplicate.json"

[<Test>]
let ``Marketplace.GenericErrorMarketplaceGenericError`` () = roundTrip<MarketplaceGenericError> "MarketplaceMessages/MarketplaceGenericError.json"

[<Test>]
let ``Marketplace.SubmissionErrorMarketplaceErrorDuplicate`` () = roundTrip<MarketplaceSubmissionError> "MarketplaceMessages/MarketplaceErrorDuplicate.json"

[<Test>]
let ``Marketplace.SubmissionResultMarketplaceErrorDuplicate`` () = roundTrip<MarketplaceSubmissionResult> "MarketplaceMessages/MarketplaceErrorDuplicate.json"

[<Test>]
let ``Marketplace.SubmissionErrorMarketplaceGenericError`` () = roundTrip<MarketplaceSubmissionError> "MarketplaceMessages/MarketplaceGenericError.json"

[<Test>]
let ``Marketplace.SubmissionResultMarketplaceGenericError`` () = roundTrip<MarketplaceSubmissionResult> "MarketplaceMessages/MarketplaceGenericError.json"

[<Test>]
let ``Marketplace.BatchRequest`` () = roundTrip<MarketplaceBatchRequest> "MarketplaceMessages/MarketplaceBatchRequest.json"

[<Test>]
let ``Marketplace.BatchResponseDTO`` () = roundTrip<MarketplaceBatchResponseDTO> "MarketplaceMessages/MarketplaceBatchResponseDTO.json"

[<Test>]
let ``InternalMessages.MeteringUpdateEvent`` () = 
    [
        "usageReported"
        "usageReportedOnlyResourceUri"
        "usageReportedOnlyResourceId"
        "subscriptionDeleted1"
        "subscriptionDeleted2"
        "subscriptionDeleted3"
        "subscriptionPurchased"
    ]
    |> List.iter (fun name -> roundTrip<MeteringUpdateEvent> $"InternalMessages/{name}.json")
