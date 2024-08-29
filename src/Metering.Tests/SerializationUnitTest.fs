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

    // printfn "--------------------------- %s \n%A" filename t1

    let t2: 'T =
        t1
        |> Json.toStr 0
        //|> (fun s -> printfn "%s" s; s)
        |> Json.fromStr<'T>

    Assert.That(t1, Is.EqualTo(t2), message = $"Inputfile: data/{filename}")

[<Test>]
let ``InternalMessages.AllInOne`` () =
    roundTrip<MeteringUpdateEvent list> "InternalMessages/AllInOne-SDK-Sent-Messages.json"
    roundTrip<MeteringUpdateEvent list> "InternalMessages/AllInOne-InternalMessages.json"

[<Test>]
let ``InternalMessages.Ping TopOfHour`` () = roundTrip<MeteringUpdateEvent> "InternalMessages/Ping TopOfHour.json"

[<Test>]
let ``InternalMessages.Ping ProcessingStarting`` () = roundTrip<MeteringUpdateEvent> "InternalMessages/Ping ProcessingStarting.json"

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
let ``InternalMessages.RemoveUnprocessedMessages all`` () = roundTrip<MeteringUpdateEvent> "InternalMessages/RemoveUnprocessedMessages all.json"

[<Test>]
let ``InternalDataStructures.MeteringDateTime list`` () = roundTrip<MeteringDateTime list> "InternalDataStructures/MeteringDateTime list.json"

[<Test>]
let ``InternalDataStructures.Quantity list`` () = roundTrip<Quantity list> "InternalDataStructures/Quantity list.json"

[<Test>]
let ``InternalDataStructures.RenewalInterval list`` () = roundTrip<RenewalInterval list> "InternalDataStructures/RenewalInterval list.json"

[<Test>]
let ``InternalDataStructures.MessagePosition`` () = roundTrip<MessagePosition> "InternalDataStructures/MessagePosition.json"

[<Test>]
let ``InternalDataStructures.ConsumedQuantity`` () = roundTrip<ConsumedQuantity> "InternalDataStructures/ConsumedQuantity.json"

[<Test>]
let ``InternalDataStructures.IncludedQuantity`` () = roundTrip<IncludedQuantity> "InternalDataStructures/IncludedQuantity.json"

[<Test>]
let ``InternalDataStructures.SimpleMeterValue list`` () = roundTrip<SimpleMeterValue list> "InternalDataStructures/SimpleMeterValue list.json"

[<Test>]
let ``InternalDataStructures.SimpleBillingDimension list`` () = roundTrip<SimpleBillingDimension list> "InternalDataStructures/SimpleBillingDimension list.json"

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
    Assert.That(p.PlanId.value, Is.EqualTo("the_plan"))

    let check (appInternalName: string) (expected: Quantity) =
        let actual =
            p.BillingDimensions
            |> Map.find (appInternalName |> ApplicationInternalMeterName.create)
            |> function
                | SimpleBillingDimension dim -> dim.IncludedQuantity
                | _ -> failwith $"Should have been a {nameof(SimpleBillingDimension)}"

        Assert.That(actual, Is.EqualTo(expected))

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

//[<Test>]
//let ``Marketplace.MarketplaceExpiredErrorResponse`` () = roundTrip<MarketplaceSubmissionError> "MarketplaceMessages/MarketplaceExpiredErrorResponse.json"

[<Test>]
let ``Marketplace.BatchRequest`` () = roundTrip<MarketplaceBatchRequest> "MarketplaceMessages/MarketplaceBatchRequest.json"

[<Test>]
let ``Marketplace.BatchResponseDTO`` () = roundTrip<MarketplaceBatchResponseDTO> "MarketplaceMessages/MarketplaceBatchResponseDTO.json"

[<Test>]
let ``EventHub.Avro`` () =
    [
        "p9--2022-12-09--16-50-12.avro"
    ]
    |> List.iter (fun filename ->
        let events =
            filename
            |> (fun n -> $"data/Capture/{filename}")
            |> File.OpenRead
            |> Metering.EventHub.CaptureProcessor.ReadEventDataFromAvroStream $"https://whatever.blob.core.windows.net/whatever-capture/whatever/metering/{filename}"
            |> Seq.map (fun e ->
                //printfn "%s %s %d %s" filename e.PartitionKey e.SequenceNumber (System.Text.Encoding.UTF8.GetString(e.Body.ToArray()))
                e
            )
            |> Seq.map Metering.EventHub.CaptureProcessor.toMeteringUpdateEvent
            |> Seq.map (fun e ->
                printfn "%s %s %A" filename e.partitionKey e

                e
            )

        Assert.That(events |> Seq.length > 0, Is.True)
    )
