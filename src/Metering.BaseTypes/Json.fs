// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

module Json =
    open System
    open System.Runtime.InteropServices
    open NodaTime.Text
    open Thoth.Json.Net
    open Metering.BaseTypes.EventHub
    open Metering.BaseTypes.WaterfallTypes

    module JsonInternals =
        module internal JsonUtil =
            let toEncoder (encode: 'T -> (string * JsonValue) list) : Encoder<'T> =
                encode >> Encode.object

            let toDecoder (decode: Decode.IGetters -> 'T) : Decoder<'T> =
                decode |> Decode.object

            let createEncoderDecoder<'T> (encode: 'T -> (string * JsonValue) list) (decode: Decode.IGetters -> 'T) : Encoder<'T> * Decoder<'T> =
                (encode |> toEncoder, decode |> toDecoder)

            let encodeOption<'T> (name: string) (encoder: Encoder<'T>) (t: 'T option) : (string * JsonValue) list =
                match t with
                | None -> []
                | Some v -> [ (name, v |> encoder) ]

            let withCustom<'T> (encode: ('T -> (string * JsonValue) list)) (decode : (Decode.IGetters -> 'T)) (extra: ExtraCoders) : ExtraCoders =
                extra
                |> Extra.withCustom
                    (encode |> toEncoder)
                    (decode |> toDecoder)

        module MeteringDateTime =
            let private makeEncoder<'T> (pattern : IPattern<'T>) : Encoder<'T> = pattern.Format >> Encode.string
            let private makeDecoder<'T> (pattern : IPattern<'T>) : Decoder<'T> =
                Decode.string
                |> Decode.andThen (fun v ->
                    let x = pattern.Parse(v)
                    if x.Success
                    then x.Value |> Decode.succeed
                    else (sprintf "Failed to decode `%s`" v) |> Decode.fail
                )

            // Use the first pattern as default, therefore the `|> List.head`
            let Encoder : Encoder<MeteringDateTime> = MeteringDateTime.meteringDateTimePatterns |> List.head |> makeEncoder

            // This supports decoding of multiple formats on how Date and Time could be represented, therefore the `|> Decode.oneOf`
            let Decoder : Decoder<MeteringDateTime> = MeteringDateTime.meteringDateTimePatterns |> List.map makeDecoder |> Decode.oneOf

        module Quantity =
            let InfiniteStr = "Infinite"

            let Encoder (x: Quantity) : JsonValue =
                match x with
                | MeteringInt i -> i |> Encode.uint32
                | MeteringFloat f -> f |> Encode.float
                | Infinite -> InfiniteStr |> Encode.string

            let Decoder : Decoder<Quantity> =
                let decodeStringQuantity s =
                    if s = InfiniteStr
                    then Infinite |> Decode.succeed
                    else
                        if s.Contains(".")
                        then
                            match s |> Double.TryParse with
                            | false, _ -> (sprintf "Failed to decode `%s`" s) |> Decode.fail
                            | _, v -> v |> Quantity.create |> Decode.succeed
                        else
                            match s |> UInt32.TryParse with
                            | false, _ -> (sprintf "Failed to decode `%s`" s) |> Decode.fail
                            | _, v -> v |> Quantity.create |> Decode.succeed

                [
                    Decode.uint32 |> Decode.andThen(Quantity.create >> Decode.succeed)
                    Decode.float |> Decode.andThen(Quantity.create >> Decode.succeed)
                    Decode.string |> Decode.andThen(decodeStringQuantity)
                ] |> Decode.oneOf

        module MessagePosition =
            let (partitionId, sequenceNumber, partitionTimestamp) =
                ("partitionId", "sequenceNumber", "partitionTimestamp")

            let encode (x: MessagePosition) : (string * JsonValue) list =
                [
                    (partitionId, x.PartitionID.value |> Encode.string)
                    (sequenceNumber, x.SequenceNumber |> Encode.int64)
                    (partitionTimestamp, x.PartitionTimestamp |> MeteringDateTime.Encoder)
                ]

            let decode (get: Decode.IGetters) : MessagePosition =
                {
                    PartitionID = (get.Required.Field partitionId Decode.string) |> PartitionID.create
                    SequenceNumber = get.Required.Field sequenceNumber Decode.int64
                    PartitionTimestamp = get.Required.Field partitionTimestamp MeteringDateTime.Decoder
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module RenewalInterval =
            let Encoder (x: RenewalInterval) =
                match x with
                | Monthly -> nameof(Monthly) |> Encode.string
                | Annually -> nameof(Annually) |> Encode.string
                | TwoYears -> "2-years" |> Encode.string
                | ThreeYears -> "3-years" |> Encode.string

            let Decoder : Decoder<RenewalInterval> =
                Decode.string |> Decode.andThen (
                   function
                   | nameof(Monthly) -> Decode.succeed Monthly
                   | nameof(Annually) -> Decode.succeed Annually
                   | "2-years" -> Decode.succeed TwoYears
                   | "3-years" -> Decode.succeed ThreeYears
                   | invalid -> Decode.fail (sprintf "Failed to decode `%s`" invalid))

        module ConsumedQuantity =
            let (consumedQuantity, total, lastUpdate) =
                ("consumedQuantity", "total", "lastUpdate")

            let encode (x: ConsumedQuantity) : (string * JsonValue) list =
                [
                    (consumedQuantity, x.CurrentHour |> Quantity.Encoder)
                    (total, x.BillingPeriodTotal |> Quantity.Encoder)
                    (lastUpdate, x.LastUpdate |> MeteringDateTime.Encoder)
                ]

            let decode (get: Decode.IGetters) : ConsumedQuantity =
                {
                    CurrentHour = get.Required.Field consumedQuantity Quantity.Decoder
                    BillingPeriodTotal = get.Required.Field total Quantity.Decoder
                    LastUpdate = get.Required.Field lastUpdate MeteringDateTime.Decoder
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module IncludedQuantity =
            let (remaining, consumedTotal, lastUpdate) = ("quantity", "consumed", "lastUpdate")

            let encode (x: IncludedQuantity) : (string * JsonValue) list =
                [
                    (remaining, x.RemainingQuantity |> Quantity.Encoder)
                    (consumedTotal, x.BillingPeriodTotal |> Quantity.Encoder)
                    (lastUpdate, x.LastUpdate |> MeteringDateTime.Encoder)
                ]

            let decode (get: Decode.IGetters) : IncludedQuantity =
                {
                    RemainingQuantity = get.Required.Field remaining Quantity.Decoder
                    BillingPeriodTotal = get.Optional.Field consumedTotal Quantity.Decoder |> Option.defaultValue Quantity.Zero // Introducing this as an optional field, assuming 0 if not there.
                    LastUpdate = get.Required.Field lastUpdate MeteringDateTime.Decoder
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module SimpleMeterValue =
            let (consumed, included) = ("consumed", "included")

            let Encoder (x: SimpleMeterValue) : JsonValue =
                match x with
                    | ConsumedQuantity q -> [ ( consumed, q |> ConsumedQuantity.Encoder ) ]
                    | IncludedQuantity q -> [ ( included, q |> IncludedQuantity.Encoder ) ]
                |> Encode.object

            let Decoder : Decoder<SimpleMeterValue> =
                [
                    Decode.field consumed ConsumedQuantity.Decoder |> Decode.map ConsumedQuantity
                    Decode.field included IncludedQuantity.Decoder |> Decode.map IncludedQuantity
                ] |> Decode.oneOf

        module SimpleBillingDimension =
            // { "name": "nde", "type": "simple", "dimension": "nodecharge", "included": 1000 }
            let (tYpe, dimension, included, meter) = ("type", "dimension", "included", "meter");
            let encode (x: SimpleBillingDimension) : (string * JsonValue) list =
                [
                    (dimension, x.DimensionId.value |> Encode.string)
                ]
                |> (fun l ->
                    if x.IncludedQuantity = Quantity.Zero
                    then l
                    else  (included, x.IncludedQuantity |> Quantity.Encoder) :: l)
                |> (fun l ->
                    match x.Meter with
                    | None -> l
                    | Some m -> (meter, m |> SimpleMeterValue.Encoder) :: l)

            let decode (get: Decode.IGetters) : SimpleBillingDimension =
                {
                    DimensionId = (get.Required.Field dimension Decode.string) |> DimensionId.create
                    IncludedQuantity = get.Optional.Field included Quantity.Decoder |> Option.defaultValue Quantity.Zero
                    Meter = get.Optional.Field meter SimpleMeterValue.Decoder
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module WaterfallMeterValue =
            let (total, consumption, model, lastUpdate) = ("total", "consumption", "model", "lastUpdate")

            let encode (x: WaterfallMeterValue) : (string * JsonValue) list =
                [
                    (total, x.Total |> Quantity.Encoder)
                    (consumption, x.Consumption |> Map.toList |> List.map (fun (dimensionId, quantity) -> (dimensionId.value, quantity |> Quantity.Encoder)) |> Encode.object) // serialize as a Dictionary
                    (lastUpdate, x.LastUpdate |> MeteringDateTime.Encoder)
                ]

            let decode (get: Decode.IGetters) : WaterfallMeterValue =
                let turnKeyIntoSubscriptionType (k, v) = (k |> DimensionId.create, v)

                {
                    Total = get.Required.Field total Quantity.Decoder
                    Consumption = get.Required.Field consumption ((Decode.keyValuePairs Quantity.Decoder) |> Decode.andThen (fun r -> r |> List.map turnKeyIntoSubscriptionType |> Map.ofList |> Decode.succeed))
                    LastUpdate = get.Required.Field lastUpdate MeteringDateTime.Decoder
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module WaterfallBillingDimensionItem =
            open Metering.BaseTypes.WaterfallTypes

            let (dimension, threshold) = ("dimension", "threshold");

            let encode (x: WaterfallBillingDimensionItem) : (string * JsonValue) list =
                [
                    (dimension, x.DimensionId.value |> Encode.string)
                    (threshold, x.Threshold |> Quantity.Encoder)
                ]

            let decode (get: Decode.IGetters) : WaterfallBillingDimensionItem =
                {
                    DimensionId = (get.Required.Field dimension Decode.string) |> DimensionId.create
                    Threshold = get.Required.Field threshold Quantity.Decoder
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module WaterfallBillingDimension =
            let (meter, tiers) = ("meter", "tiers")

            let encode (x: WaterfallBillingDimension) : (string * JsonValue) list =
                [
                    (tiers, x.Tiers |> List.map (fun x -> x |> WaterfallBillingDimensionItem.Encoder) |> Encode.list)
                ]
                |> (fun l ->
                    match x.Meter with
                    | None -> l
                    | Some m -> (meter, m |> WaterfallMeterValue.Encoder) :: l)

            let decode (get: Decode.IGetters) : WaterfallBillingDimension =
                WaterfallMeterLogic.createBillingDimension
                     (get.Required.Field tiers (Decode.list WaterfallBillingDimensionItem.Decoder))
                     (get.Optional.Field meter WaterfallMeterValue.Decoder)

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module BillingDimension =
            let (tYpe) = ("type")
            let encode (x: BillingDimension) : (string * JsonValue) list =
                let setType (t: string) (l: (string * JsonValue) list) : (string * JsonValue) list =
                     (tYpe, t |> Encode.string) :: l

                match x with
                | SimpleBillingDimension s -> s |> SimpleBillingDimension.encode |> setType "simple"
                | WaterfallBillingDimension w -> w |> WaterfallBillingDimension.encode |> setType "waterfall"

            let decode (get: Decode.IGetters) : BillingDimension =
                let t = get.Required.Field tYpe Decode.string
                match t with
                | "waterfall" -> get |> WaterfallBillingDimension.decode |> WaterfallBillingDimension
                | "simple" -> get |> SimpleBillingDimension.decode |> SimpleBillingDimension
                | unknown -> failwith $"Unknown type {unknown}"

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module BillingDimensions =
            let Encoder (x: BillingDimensions) =
                x
                |> Map.toList
                |> List.map (fun (d, m) -> (d.value, m |> BillingDimension.Encoder))
                |> Encode.object

            let Decoder : Decoder<BillingDimensions> =
                let turnKeyIntoApplicationInternalMeterName (k, v) =  (k |> ApplicationInternalMeterName.create, v)
                (Decode.keyValuePairs BillingDimension.Decoder) |> Decode.andThen (fun r -> r |> List.map turnKeyIntoApplicationInternalMeterName |> Map.ofList |> Decode.succeed)

        module Plan =
            let (planId, billingDimensions) = ("planId", "billingDimensions")

            let encode (x: Plan) : (string * JsonValue) list =
                [
                    (planId, x.PlanId.value |> Encode.string)
                    (billingDimensions, x.BillingDimensions |> BillingDimensions.Encoder)
                ]

            let decode (get: Decode.IGetters) : Plan =
                let turnKeyIntoDimensionId (k, v) =  (k |> DimensionId.create, v)

                {
                    PlanId = (get.Required.Field planId Decode.string) |> PlanId.create
                    BillingDimensions = get.Required.Field billingDimensions BillingDimensions.Decoder
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module MarketplaceResourceId =
            let (resourceId, resourceUri, internalResourceId) =
                ("resourceId", "resourceUri", "internalResourceId");

            let encode (x: MarketplaceResourceId) : (string * JsonValue) list =
                []
                |> (fun l ->
                    match x.ResourceURI with
                    | None -> l
                    | Some u -> (resourceUri, u |> Encode.string) :: l)
                |> (fun l ->
                    match x.ResourceID with
                    | None -> l
                    | Some u -> (resourceId, u |> Encode.string) :: l)

            let decode (get: Decode.IGetters) : MarketplaceResourceId =
                let res = {
                    ResourceID = get.Optional.Field resourceId Decode.string
                    ResourceURI = get.Optional.Field resourceUri Decode.string
                }
                let internalId = get.Optional.Field internalResourceId Decode.string

                match (res, internalId) with
                | ({ ResourceID = None; ResourceURI = None }, Some internalId) -> MarketplaceResourceId.fromStr internalId
                | ({ ResourceID = None; ResourceURI = None }, None) -> failwith $"Missing {resourceId} or {resourceUri} field"
                | _ -> res

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module InternalUsageEvent =
            let (timestamp, meterName, quantity, properties) =
                ("timestamp", "meterName", "quantity", "properties");

            let encode (x: InternalUsageEvent) : (string * JsonValue) list =
                (x.MarketplaceResourceId |> MarketplaceResourceId.encode)
                |> List.append [
                    (timestamp, x.Timestamp |> MeteringDateTime.Encoder)
                    (meterName, x.MeterName.value |> Encode.string)
                    (quantity, x.Quantity |> Quantity.Encoder)
                ]
                |> (fun l ->
                    match x.Properties with
                    | Some props when props.Count > 0 ->
                        (
                            properties,
                            props
                            |> Map.toSeq
                            |> Seq.toList<string * string>
                            |> List.map (fun (k,v) -> (k, v |> Encode.string))
                            |> Encode.object
                        ) :: l
                    | _ -> l
                )

            let decode (get: Decode.IGetters) : InternalUsageEvent =
                let DecodeProperties : Decoder<Map<string,string>> =
                    (Decode.keyValuePairs Decode.string)
                    |> Decode.andThen (Map.ofList >> Decode.succeed)

                let properties =
                    match get.Optional.Field properties DecodeProperties with
                    | Some props when props.Count > 0 -> Some props
                    | _ -> None

                {
                    MarketplaceResourceId = get |> MarketplaceResourceId.decode
                    Timestamp = get.Required.Field timestamp MeteringDateTime.Decoder
                    MeterName = (get.Required.Field meterName Decode.string) |> ApplicationInternalMeterName.create
                    Quantity = get.Required.Field quantity Quantity.Decoder
                    Properties = properties
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module Subscription =
            let (plan, renewalInterval, subscriptionStart) =
                ("plan", "renewalInterval", "subscriptionStart");

            let encode (x: Subscription) : (string * JsonValue) list =
                List.append
                    (x.MarketplaceResourceId |> MarketplaceResourceId.encode)
                    [
                        (subscriptionStart, x.SubscriptionStart |> MeteringDateTime.Encoder)
                        (renewalInterval, x.RenewalInterval |> RenewalInterval.Encoder)
                        (plan, x.Plan |> Plan.Encoder)
                    ]

            let decode (get: Decode.IGetters) : Subscription =
                {
                    Plan = get.Required.Field plan Plan.Decoder
                    RenewalInterval = get.Required.Field renewalInterval RenewalInterval.Decoder
                    SubscriptionStart = get.Required.Field subscriptionStart MeteringDateTime.Decoder
                    MarketplaceResourceId = get |> MarketplaceResourceId.decode
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module SubscriptionUpdate =
            let (updatedplan) = ("updatedplan");

            let encode (x: SubscriptionUpdate) : (string * JsonValue) list =
                List.append
                    (x.MarketplaceResourceId |> MarketplaceResourceId.encode)
                    [
                        (updatedplan, x.UpdatedPlan |> Plan.Encoder)
                    ]

            let decode (get: Decode.IGetters) : SubscriptionUpdate =
                {
                    UpdatedPlan = get.Required.Field updatedplan Plan.Decoder
                    MarketplaceResourceId = get |> MarketplaceResourceId.decode
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module SubscriptionCreationInformation =
            let (subscription) = ("subscription");

            let encode (x: SubscriptionCreationInformation) : (string * JsonValue) list =
                [
                    (subscription, x.Subscription |> Subscription.Encoder)
                ]

            let decode (get: Decode.IGetters) =
                {
                    Subscription = get.Required.Field subscription Subscription.Decoder
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module Marketplace =
            module MarketplaceRequest =
                let (quantity, planId, dimensionId, effectiveStartTime) =
                    ("quantity", "planId", "dimension", "effectiveStartTime");

                let encode (x: MarketplaceRequest) : (string * JsonValue) list =
                    [
                        (effectiveStartTime, x.EffectiveStartTime |> MeteringDateTime.Encoder)
                        (planId, x.PlanId.value |> Encode.string)
                        (dimensionId, x.DimensionId.value |> Encode.string)
                        (quantity, x.Quantity.AsFloat |> Encode.float)
                    ]
                    |> List.append (x.MarketplaceResourceId |> MarketplaceResourceId.encode)

                let decode (get: Decode.IGetters) =
                    {
                        MarketplaceResourceId = get |> MarketplaceResourceId.decode
                        EffectiveStartTime = get.Required.Field effectiveStartTime MeteringDateTime.Decoder
                        PlanId = (get.Required.Field planId Decode.string) |> PlanId.create
                        DimensionId = (get.Required.Field dimensionId Decode.string) |> DimensionId.create
                        Quantity = get.Required.Field quantity Quantity.Decoder
                    }

                let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

            module MarketplaceBatchRequest =
                let (request) =
                    ("request")

                let encode (x: MarketplaceBatchRequest) = [ (request, x.values |> List.map MarketplaceRequest.Encoder |> Encode.list) ]

                let decode (get: Decode.IGetters) = (get.Required.Field request (Decode.list MarketplaceRequest.Decoder)) |> MarketplaceBatchRequest.createBatch

            module SubmissionStatus =
                let private ValEncoder (x: SubmissionStatus) : JsonValue =
                    match x with
                    | SubmissionStatus.Accepted -> "Accepted"
                    | SubmissionStatus.Expired -> "Expired"
                    | SubmissionStatus.Duplicate -> "Duplicate"
                    | SubmissionStatus.UsageEventError -> "UsageEventError"
                    | SubmissionStatus.ResourceNotFound -> "ResourceNotFound"
                    | SubmissionStatus.ResourceNotAuthorized -> "ResourceNotAuthorized"
                    | SubmissionStatus.ResourceNotActive -> "ResourceNotActive"
                    | SubmissionStatus.InvalidDimension -> "InvalidDimension"
                    | SubmissionStatus.InvalidQuantity -> "InvalidQuantity"
                    | SubmissionStatus.BadArgument -> "BadArgument"
                    |> Encode.string

                let private ValDecoder : Decoder<SubmissionStatus> =
                    Decode.string
                    |> Decode.andThen(fun x ->
                        match x with
                        | "Accepted" -> SubmissionStatus.Accepted |> Decode.succeed
                        | "Expired" -> SubmissionStatus.Expired |> Decode.succeed
                        | "Duplicate" -> SubmissionStatus.Duplicate |> Decode.succeed
                        | "UsageEventError" -> SubmissionStatus.UsageEventError |> Decode.succeed
                        | "ResourceNotFound" -> SubmissionStatus.ResourceNotFound |> Decode.succeed
                        | "ResourceNotAuthorized" -> SubmissionStatus.ResourceNotAuthorized |> Decode.succeed
                        | "ResourceNotActive" -> SubmissionStatus.ResourceNotActive |> Decode.succeed
                        | "InvalidDimension" -> SubmissionStatus.InvalidDimension |> Decode.succeed
                        | "InvalidQuantity" -> SubmissionStatus.InvalidQuantity |> Decode.succeed
                        | "BadArgument" -> SubmissionStatus.BadArgument |> Decode.succeed
                        | unknown -> (sprintf "Failed to decode `%s`" unknown) |> Decode.fail
                     )

                let status = "status"

                let encode (x: SubmissionStatus) : (string * JsonValue) list = [ status, x |> ValEncoder ]

                let decode (get: Decode.IGetters) = get.Required.Field status ValDecoder

            module MessageTime =
                let (messageTime) =
                    ("messageTime")

                let encode x = [ (messageTime, x |> MeteringDateTime.Encoder) ]

                let decode (get: Decode.IGetters) = get.Required.Field messageTime MeteringDateTime.Decoder

            module UsageEventID =
                let (usageEventId) = ("usageEventId")

                let encode (x: UsageEventID option) = x |> JsonUtil.encodeOption usageEventId Encode.string

                let decode (get: Decode.IGetters) = get.Optional.Field usageEventId Decode.string

            module MarketplaceSubmissionStatus =
                // A composed type, where various types are represented in the same JSON object
                let encode (x: MarketplaceSubmissionStatus) =
                    [
                        x.Status |> SubmissionStatus.encode
                        x.MessageTime |> MessageTime.encode
                        x.UsageEventID |> UsageEventID.encode
                    ]
                    |> List.concat

                let decode (get: Decode.IGetters) =
                    {
                        Status = get |> SubmissionStatus.decode
                        MessageTime = get |> MessageTime.decode
                        UsageEventID = get |> UsageEventID.decode
                    }

            module MarketplaceError =
                let (code, target, message) = ("code", "target", "message")

                let encode (x: MarketplaceError) =
                    [
                        (code, x.Code |> Encode.string)
                        (message, x.Message |> Encode.string)
                    ]
                    |> (fun l ->
                        match x.Target with
                        | None -> l
                        | Some t -> (target, t |> Encode.string) :: l)


                let decode (get: Decode.IGetters) =
                    {
                        Code = get.Required.Field code Decode.string
                        Target = get.Optional.Field target Decode.string
                        Message = get.Required.Field message Decode.string
                    }

                let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

            module MarketplaceSuccessResponse =
                let encode (x: MarketplaceSuccessResponse) =
                    [
                        x.RequestData |> MarketplaceRequest.encode
                        x.Status |> MarketplaceSubmissionStatus.encode
                    ]
                    |> List.concat

                let decode (get: Decode.IGetters) =
                    {
                        RequestData = get |> MarketplaceRequest.decode
                        Status = get |> MarketplaceSubmissionStatus.decode
                    }

                let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

            module MarketplaceErrorDuplicate =
                let encode (x: MarketplaceErrorDuplicate) =
                    [
                        x.FailedRequest |> MarketplaceRequest.encode
                        x.FailureStatus |> MarketplaceSubmissionStatus.encode
                        [ "error", [
                            "additionalInfo", [
                                "acceptedMessage",
                                    x.PreviouslyAcceptedMessage |> MarketplaceSuccessResponse.Encoder
                                ] |> Encode.object
                            ] |> Encode.object
                        ]
                    ] |> List.concat

                let decode (get: Decode.IGetters) =
                    {
                        FailedRequest = get |> MarketplaceRequest.decode
                        FailureStatus = get |> MarketplaceSubmissionStatus.decode
                        PreviouslyAcceptedMessage = get.Required.At [ "error"; "additionalInfo"; "acceptedMessage" ] MarketplaceSuccessResponse.Decoder
                    }

            module MarketplaceGenericError =
                let encode (x: MarketplaceGenericError) : (string * JsonValue) list =
                    [
                        x.RequestData |> MarketplaceRequest.encode
                        x.Status |> MarketplaceSubmissionStatus.encode
                        [ "error",
                            [
                                x.Error |> MarketplaceError.encode
                                [ "details", x.ErrorDetails |> List.map MarketplaceError.Encoder |> Encode.list ]
                            ] |> List.concat |> Encode.object
                        ]
                    ] |> List.concat

                let decode (get: Decode.IGetters) : MarketplaceGenericError =
                    {
                        RequestData = get |> MarketplaceRequest.decode
                        Status = get |> MarketplaceSubmissionStatus.decode
                        Error = get.Required.At [ "error" ] MarketplaceError.Decoder
                        ErrorDetails = get.Required.At [ "error"; "details" ] (Decode.list MarketplaceError.Decoder)
                    }

            module MarketplaceSubmissionError =
                let encode (x: MarketplaceSubmissionError) : (string * JsonValue) list =
                    match x with
                    | DuplicateSubmission s -> s |> MarketplaceErrorDuplicate.encode
                    | ResourceNotFound e -> e |> MarketplaceGenericError.encode
                    | Expired e -> e |> MarketplaceGenericError.encode
                    | Generic e -> e |> MarketplaceGenericError.encode

                let decode (get: Decode.IGetters) : MarketplaceSubmissionError =
                    let statusVal = get.Required.Field "status" Decode.string
                    match statusVal with
                    | "Duplicate" -> get |> MarketplaceErrorDuplicate.decode |> DuplicateSubmission
                    | "ResourceNotFound" -> get |> MarketplaceGenericError.decode |> ResourceNotFound
                    | "Expired" -> get |> MarketplaceGenericError.decode |> Expired
                    | _ -> get |> MarketplaceGenericError.decode |> Generic

                let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

            module MarketplaceSubmissionResult =
                let encode (x: MarketplaceSubmissionResult) : (string * JsonValue) list =
                    match x with
                    | Ok s -> s |> MarketplaceSuccessResponse.encode
                    | Error e -> e |> MarketplaceSubmissionError.encode

                let Encoder = JsonUtil.toEncoder encode

                let Decoder : Decoder<MarketplaceSubmissionResult> =
                    [
                        MarketplaceSubmissionError.Decoder |> Decode.andThen(Error >> Decode.succeed)
                        MarketplaceSuccessResponse.Decoder |> Decode.andThen(Ok >> Decode.succeed)
                    ] |> Decode.oneOf

            module AzureHttpResponseHeaders =
                let (msrequestid, mscorrelationid) =
                    ("xMsRequestId", "xMsCorrelationId");

                let encode (x: AzureHttpResponseHeaders) : (string * JsonValue) list =
                    [
                        (msrequestid, x.RequestID |> Encode.string)
                        (mscorrelationid, x.CorrelationID |> Encode.string)
                    ]

                let decode (get: Decode.IGetters) : AzureHttpResponseHeaders =
                    {
                        RequestID = get.Required.Field msrequestid Decode.string
                        CorrelationID =  get.Required.Field mscorrelationid Decode.string
                    }

                let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

            module MarketplaceBatchResponseDTO =
                let (result, count) = ("result", "count")

                let encode (x: MarketplaceBatchResponseDTO) : (string * JsonValue) list =
                     [
                        (count, x.Results.Length |> Encode.int)
                        (result, x.Results |> List.map MarketplaceSubmissionResult.Encoder |> Encode.list)
                     ]

                let decode (get: Decode.IGetters) : MarketplaceBatchResponseDTO =
                    let r = get.Required.Field result (Decode.list MarketplaceSubmissionResult.Decoder)
                    let count = get.Required.Field count Decode.int
                    if count <> r.Length
                    then failwith $"Failed to decode {nameof(MarketplaceBatchResponse)}: Number of elements in content was {r.Length} instead of {count}"
                    else { Results = r }

            module MarketplaceResponse =
                let (httpHeaders, result) = ("httpHeaders", "result")
                let encode (x: MarketplaceResponse) : (string * JsonValue) list =
                    [
                        (httpHeaders, x.Headers |> AzureHttpResponseHeaders.Encoder)
                        (result, x.Result |> MarketplaceSubmissionResult.Encoder)
                    ]
                let decode (get: Decode.IGetters) : MarketplaceResponse =
                    {
                        Headers = get.Required.Field httpHeaders AzureHttpResponseHeaders.Decoder
                        Result =  get.Required.Field result MarketplaceSubmissionResult.Decoder
                    }
                let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module Meter =
            open Marketplace

            let (subscription, currentMeters, usageToBeReported, lastProcessedMessage, deletionRequested) =
                ("subscription", "currentMeters", "usageToBeReported", "lastProcessedMessage", "deletionRequested");

            let encode (x: Meter) : (string * JsonValue) list =
                [
                    (subscription, x.Subscription |> Subscription.Encoder)
                    (usageToBeReported, x.UsageToBeReported |> List.map MarketplaceRequest.Encoder |> Encode.list)
                    (lastProcessedMessage, x.LastProcessedMessage |> MessagePosition.Encoder)
                ]
                |> (fun l ->
                    if x.DeletionRequested
                    then (deletionRequested, x.DeletionRequested |> Encode.bool) :: l
                    else l
                )

            let decode (get: Decode.IGetters) : Meter =
                {
                    Subscription = get.Required.Field subscription Subscription.Decoder
                    UsageToBeReported = get.Required.Field usageToBeReported (Decode.list MarketplaceRequest.Decoder)
                    LastProcessedMessage = get.Required.Field lastProcessedMessage MessagePosition.Decoder
                    DeletionRequested = get.Optional.Field deletionRequested Decode.bool |> Option.defaultValue false
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module UnprocessableMessage =
            let (t, v) = ("type", "value")

            let encode (x: UnprocessableMessage) : (string * JsonValue) list =
                match x with
                | UnprocessableStringContent s ->
                    [
                        (t, "string" |> Encode.string)
                        (v, s |> Encode.string)
                    ]
                | UnprocessableByteContent b ->
                    [
                        (t, "bytes" |> Encode.string)
                        (v, Convert.ToBase64String(b) |> Encode.string)
                    ]
                | UnprocessableUsageEvent u ->
                    [
                        (t, "usageEvent" |> Encode.string)
                        (v, u |> InternalUsageEvent.Encoder)
                    ]

            let decode (get: Decode.IGetters) : UnprocessableMessage =
                match (get.Required.Field t Decode.string) with
                | "string" -> (get.Required.Field v Decode.string) |> UnprocessableStringContent
                | "bytes" -> (get.Required.Field v Decode.string) |> Convert.FromBase64String |> UnprocessableByteContent
                | "usageEvent" -> (get.Required.Field v InternalUsageEvent.Decoder) |> UnprocessableUsageEvent
                | unsupported -> failwith $"Could not decode {nameof(UnprocessableMessage)}, unknown type {unsupported}"

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module RemoveUnprocessedMessages =
            let encodeSelection = function
                | BeforeIncluding x -> ("beforeIncluding", x |> Encode.int64)
                | Exactly x -> ("exactly", x |> Encode.int64)

            let Encoder ({PartitionID = partitionID; Selection = selection}: RemoveUnprocessedMessages) : JsonValue =
                [
                    ("partitionId", partitionID.value |> Encode.string)
                    encodeSelection selection
                ] |> Encode.object

            let decodeBeforeIncluding (get: Decode.IGetters) =
                {
                    PartitionID = (get.Required.Field "partitionId" Decode.string) |> PartitionID.create
                    Selection = (get.Required.Field  "beforeIncluding" Decode.int64) |> BeforeIncluding
                }

            let decodeExactly (get: Decode.IGetters) =
                {
                    PartitionID = (get.Required.Field "partitionId" Decode.string) |> PartitionID.create
                    Selection = (get.Required.Field  "exactly" Decode.int64) |> Exactly
                }

            let Decoder : Decoder<RemoveUnprocessedMessages> =
                [ decodeBeforeIncluding; decodeExactly ]
                |> List.map Decode.object
                |> Decode.oneOf

        module PingMessage =
            let (partitionId, localTime, pingReason, sendingHost) =
                ("partitionId", "localTime", "reason", "sender");

            let reasonToStr = function
                | ProcessingStarting -> "starting"
                | TopOfHour -> "newhour"
            let strToReason = function
                | "starting" -> ProcessingStarting
                | _ -> TopOfHour

            let encode (x: PingMessage) : (string * JsonValue) list =
                [
                    (partitionId, x.PartitionID.value |> Encode.string)
                    (localTime, x.LocalTime |> MeteringDateTime.Encoder)
                    (pingReason, x.PingReason |> reasonToStr |> Encode.string)
                    (sendingHost, x.SendingHost |> Encode.string)
                ]

            let decode (get: Decode.IGetters) : PingMessage =
                {
                    PartitionID = (get.Required.Field partitionId Decode.string) |> PartitionID.create
                    LocalTime = get.Required.Field localTime MeteringDateTime.Decoder
                    PingReason = (get.Required.Field pingReason Decode.string) |> strToReason
                    SendingHost = get.Required.Field sendingHost Decode.string
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module MeteringUpdateEvent =
            open Marketplace

            let (typeid, value) = ("type", "value")

            let encodeKeyValue<'T> (k: string) (valueEncoder: Encoder<'T>) (v: 'T) =
                [
                    (typeid, k |> Encode.string)
                    (value, v |> valueEncoder)
                ]

            let encode (x: MeteringUpdateEvent) : (string * JsonValue) list =
                match x with
                | SubscriptionPurchased x -> x |> encodeKeyValue "SubscriptionPurchased" SubscriptionCreationInformation.Encoder
                | SubscriptionUpdated x -> x |> encodeKeyValue "SubscriptionUpdated" SubscriptionUpdate.Encoder
                | SubscriptionDeletion x -> x |> encodeKeyValue "SubscriptionDeleted" MarketplaceResourceId.Encoder
                | UsageReported x -> x |> encodeKeyValue "UsageReported" InternalUsageEvent.Encoder
                | UsageSubmittedToAPI x -> x |> encodeKeyValue "UsageSubmittedToAPI" MarketplaceResponse.Encoder
                | UnprocessableMessage x -> x |> encodeKeyValue "UnprocessableMessage" UnprocessableMessage.Encoder
                | RemoveUnprocessedMessages x -> x |> encodeKeyValue "RemoveUnprocessedMessages" RemoveUnprocessedMessages.Encoder
                | Ping x -> x |> encodeKeyValue "Ping" PingMessage.Encoder

            let decode (get: Decode.IGetters) : MeteringUpdateEvent =
                match (get.Required.Field typeid Decode.string) with
                | "SubscriptionPurchased" -> (get.Required.Field value SubscriptionCreationInformation.Decoder) |> SubscriptionPurchased
                | "SubscriptionUpdated" -> (get.Required.Field value SubscriptionUpdate.Decoder) |> SubscriptionUpdated
                | "SubscriptionDeleted" -> (get.Required.Field value MarketplaceResourceId.Decoder) |> SubscriptionDeletion
                | "UsageReported" -> (get.Required.Field value InternalUsageEvent.Decoder) |> UsageReported
                | "UsageSubmittedToAPI" -> (get.Required.Field value MarketplaceResponse.Decoder) |> UsageSubmittedToAPI
                | "UnprocessableMessage" -> (get.Required.Field value UnprocessableMessage.Decoder) |> UnprocessableMessage
                | "RemoveUnprocessedMessages" -> (get.Required.Field value RemoveUnprocessedMessages.Decoder) |> RemoveUnprocessedMessages
                | "Ping" -> (get.Required.Field value PingMessage.Decoder) |> Ping
                | unknownEventName -> failwith $"Cannot handle unknown {nameof MeteringUpdateEvent} of type {typeid}=\"{unknownEventName}\""

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module EventHubEvent_MeteringUpdateEvent =
            let (position, event) = ("position", "event")

            let encode (x: EventHubEvent<MeteringUpdateEvent>) : (string * JsonValue) list =
                [
                    (position, x.MessagePosition |> MessagePosition.Encoder)
                    (event, x.EventData |> MeteringUpdateEvent.Encoder)
                ]

            let decode (get: Decode.IGetters) : EventHubEvent<MeteringUpdateEvent> =
                {
                    MessagePosition = get.Required.Field position MessagePosition.Decoder
                    EventData = get.Required.Field event MeteringUpdateEvent.Decoder
                    Source = EventHub
                    EventsToCatchup = None
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode

        module MeterCollection =
            let (meters, unprocessable, lastProcessedMessage, plans) =
                ("meters", "unprocessable", "lastProcessedMessage", "plans")

            let encode (x: MeterCollection) : (string * JsonValue) list =
                [
                    //(meters, x.MeterCollection |> Map.toList |> List.map (fun (marketplaceResourceId, meter) -> (marketplaceResourceId.ToString(), meter |> Meter.Encoder)) |> Encode.object) // serialize as a Dictionary
                    (meters, x.Meters |> Seq.map(Meter.Encoder) |> Seq.toArray |> Encode.array) // serialize as an array
                    (unprocessable, x.UnprocessableMessages |> List.map EventHubEvent_MeteringUpdateEvent.Encoder |> Encode.list)
                ]
                |> (fun l ->
                    match x.LastUpdate with
                    | None -> l
                    | Some lastUpdate -> (lastProcessedMessage, lastUpdate |> MessagePosition.Encoder) :: l)

            let decode (get: Decode.IGetters) : MeterCollection =
                //let turnKeyIntoSubscriptionType (k, v) = (k |> MarketplaceResourceId.fromStr, v)

                {
                    // MeterCollection = get.Required.Field meters ((Decode.keyValuePairs Meter.Decoder) |> Decode.andThen (fun (r: (string * Meter) list) -> r |> List.map turnKeyIntoSubscriptionType |> Map.ofList |> Decode.succeed))
                    Meters = get.Required.Field meters (Decode.list Meter.Decoder)
                    UnprocessableMessages = get.Required.Field unprocessable (Decode.list EventHubEvent_MeteringUpdateEvent.Decoder)
                    LastUpdate = get.Optional.Field lastProcessedMessage MessagePosition.Decoder
                }

    open JsonInternals

    let enrich x =
        x
        |> Extra.withUInt64
        |> Extra.withInt64
        |> Extra.withCustom Marketplace.MarketplaceRequest.Encoder Marketplace.MarketplaceRequest.Decoder
        |> JsonUtil.withCustom Marketplace.MarketplaceBatchRequest.encode Marketplace.MarketplaceBatchRequest.decode
        |> JsonUtil.withCustom Marketplace.SubmissionStatus.encode Marketplace.SubmissionStatus.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceSubmissionStatus.encode Marketplace.MarketplaceSubmissionStatus.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceError.encode Marketplace.MarketplaceError.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceSuccessResponse.encode Marketplace.MarketplaceSuccessResponse.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceErrorDuplicate.encode Marketplace.MarketplaceErrorDuplicate.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceGenericError.encode Marketplace.MarketplaceGenericError.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceSubmissionError.encode Marketplace.MarketplaceSubmissionError.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceBatchResponseDTO.encode Marketplace.MarketplaceBatchResponseDTO.decode
        |> Extra.withCustom Marketplace.MarketplaceSubmissionResult.Encoder Marketplace.MarketplaceSubmissionResult.Decoder
        |> Extra.withCustom Quantity.Encoder Quantity.Decoder
        |> Extra.withCustom MeteringDateTime.Encoder MeteringDateTime.Decoder
        |> Extra.withCustom MessagePosition.Encoder MessagePosition.Decoder
        |> Extra.withCustom MarketplaceResourceId.Encoder MarketplaceResourceId.Decoder
        |> Extra.withCustom RenewalInterval.Encoder RenewalInterval.Decoder
        |> Extra.withCustom ConsumedQuantity.Encoder ConsumedQuantity.Decoder
        |> Extra.withCustom IncludedQuantity.Encoder IncludedQuantity.Decoder
        |> Extra.withCustom SimpleMeterValue.Encoder SimpleMeterValue.Decoder
        |> Extra.withCustom SimpleBillingDimension.Encoder SimpleBillingDimension.Decoder
        |> Extra.withCustom WaterfallMeterValue.Encoder WaterfallMeterValue.Decoder
        |> Extra.withCustom WaterfallBillingDimensionItem.Encoder WaterfallBillingDimensionItem.Decoder
        |> Extra.withCustom WaterfallBillingDimension.Encoder WaterfallBillingDimension.Decoder
        |> Extra.withCustom BillingDimension.Encoder BillingDimension.Decoder
        |> Extra.withCustom BillingDimensions.Encoder BillingDimensions.Decoder
        |> Extra.withCustom Plan.Encoder Plan.Decoder
        |> Extra.withCustom InternalUsageEvent.Encoder InternalUsageEvent.Decoder
        |> Extra.withCustom Subscription.Encoder Subscription.Decoder
        |> Extra.withCustom SubscriptionCreationInformation.Encoder SubscriptionCreationInformation.Decoder
        |> Extra.withCustom Meter.Encoder Meter.Decoder
        |> Extra.withCustom MeteringUpdateEvent.Encoder MeteringUpdateEvent.Decoder
        |> Extra.withCustom EventHubEvent_MeteringUpdateEvent.Encoder EventHubEvent_MeteringUpdateEvent.Decoder
        |> JsonUtil.withCustom MeterCollection.encode MeterCollection.decode

    let enriched = Extra.empty |> enrich

    let toStr ([<Optional; DefaultParameterValue(0)>] space: int) value =
        Encode.Auto.toString(space = space, value = value, extra = enriched)

    let fromStr<'T> json =
        match Decode.Auto.fromString<'T>(json, extra = enriched) with
        | Ok r -> r
        | Result.Error e ->
            failwith e

    let fromStr2<'T> json =
        Decode.Auto.fromString<'T>(json, extra = enriched)