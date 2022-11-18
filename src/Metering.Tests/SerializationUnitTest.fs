// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

module Metering.NUnitTests.Serialization

open System.IO
open NUnit.Framework
open Metering.BaseTypes
open Metering.BaseTypes.EventHub
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
let ``InternalMessages.SubscriptionPurchased`` () = roundTrip<MeteringUpdateEvent> "InternalMessages/SubscriptionPurchased.json"

[<Test>]
let ``InternalMessages.UsageReported`` () = roundTrip<MeteringUpdateEvent> "InternalMessages/UsageReported.json"

[<Test>]
let ``InternalMessages.UsageReported only resourceUri`` () = roundTrip<MeteringUpdateEvent> "InternalMessages/UsageReported only resourceUri.json"

[<Test>]
let ``InternalMessages.UsageReported only resourceId`` () = roundTrip<MeteringUpdateEvent> "InternalMessages/UsageReported only resourceId.json"

[<Test>]
let ``InternalMessages.SubscriptionDeleted`` () = roundTrip<MeteringUpdateEvent> "InternalMessages/SubscriptionDeleted.json"

[<Test>]
let ``InternalMessages.SubscriptionDeleted only resourceUri`` () = roundTrip<MeteringUpdateEvent> "InternalMessages/SubscriptionDeleted only resourceUri.json"

[<Test>]
let ``InternalMessages.SubscriptionDeleted only resourceId`` () = roundTrip<MeteringUpdateEvent> "InternalMessages/SubscriptionDeleted only resourceId.json"

[<Test>]
let ``InternalMessages.RemoveUnprocessedMessages beforeIncluding`` () = roundTrip<MeteringUpdateEvent> "InternalMessages/RemoveUnprocessedMessages beforeIncluding.json"

[<Test>]
let ``InternalMessages.RemoveUnprocessedMessages exactly`` () = roundTrip<MeteringUpdateEvent> "InternalMessages/RemoveUnprocessedMessages exactly.json"

[<Test>]
let ``InternalDataStructures.MeteringDateTime list`` () = roundTrip<MeteringDateTime list> "InternalDataStructures/MeteringDateTime list.json"

[<Test>]
let ``InternalDataStructures.Quantity list`` () = roundTrip<Quantity list> "InternalDataStructures/Quantity list.json"

[<Test>]
let ``InternalDataStructures.MessagePosition`` () = roundTrip<MessagePosition> "InternalDataStructures/MessagePosition.json"

[<Test>]
let ``InternalDataStructures.ConsumedQuantity`` () = roundTrip<ConsumedQuantity> "InternalDataStructures/ConsumedQuantity.json"

[<Test>]
let ``InternalDataStructures.IncludedQuantity`` () = roundTrip<IncludedQuantity> "InternalDataStructures/IncludedQuantity.json"

[<Test>]
let ``InternalDataStructures.SimpleMeterValue list`` () = roundTrip<SimpleMeterValue list> "InternalDataStructures/SimpleMeterValue list.json"

[<Test>]
let ``InternalDataStructures.SimpleConsumptionBillingDimension list`` () = roundTrip<SimpleConsumptionBillingDimension list> "InternalDataStructures/SimpleConsumptionBillingDimension list.json"

[<Test>]
let ``InternalDataStructures.WaterfallMeterValue`` () = roundTrip<WaterfallMeterValue> "InternalDataStructures/WaterfallMeterValue.json"

[<Test>]
let ``InternalDataStructures.WaterfallBillingDimension List`` () = roundTrip<WaterfallBillingDimension list> "InternalDataStructures/WaterfallBillingDimension list.json"

[<Test>]
let ``InternalDataStructures.MarketplaceResourceIdList list`` () = roundTrip<MarketplaceResourceId list> "InternalDataStructures/MarketplaceResourceId list.json"

[<Test>]
let ``InternalDataStructures.InternalUsageEvent list`` () = roundTrip<InternalUsageEvent list> "InternalDataStructures/InternalUsageEvent list.json"

[<Test>]
let ``InternalDataStructures.BillingDimension`` () = roundTrip<BillingDimension> "InternalDataStructures/SimpleDimension.json"

[<Test>]
let ``InternalDataStructures.BillingDimensions`` () = roundTrip<BillingDimensions> "InternalDataStructures/BillingDimensions.json"

[<Test>]
let ``InternalDataStructures.Subscription list`` () = roundTrip<Subscription list> "InternalDataStructures/Subscription list.json"

[<Test>]
let ``InternalDataStructures.Plan`` () = roundTrip<Plan> "InternalDataStructures/Plan.json"

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
let ``InternalDataStructures.MeterCollection`` () = roundTrip<MeterCollection> "InternalDataStructures/MeterCollection.json"

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

