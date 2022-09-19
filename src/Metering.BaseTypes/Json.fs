// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

module Json =
    open System
    open System.Runtime.InteropServices
    open NodaTime.Text
    open Thoth.Json.Net
    open Metering.BaseTypes.EventHub

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

    module ConsumedQuantity =
        let (consumedQuantity, created, lastUpdate) = 
            ("consumedQuantity", "created", "lastUpdate")
        
        let encode (x: ConsumedQuantity) : (string * JsonValue) list =
            [
                (consumedQuantity, x.Amount |> Quantity.Encoder)
                (created, x.Created |> MeteringDateTime.Encoder)
                (lastUpdate, x.LastUpdate |> MeteringDateTime.Encoder)
            ]

        let decode (get: Decode.IGetters) : ConsumedQuantity =
            {
                Amount = get.Required.Field consumedQuantity Quantity.Decoder
                Created = get.Required.Field created MeteringDateTime.Decoder
                LastUpdate = get.Required.Field lastUpdate MeteringDateTime.Decoder
            }

        let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode 
        
    module IncludedQuantity =
        let (quantity, created, lastUpdate) = ("quantity", "created", "lastUpdate")

        let encode (x: IncludedQuantity) : (string * JsonValue) list =
            [ 
                (quantity, x.Quantity |> Quantity.Encoder)
                (created, x.Created |> MeteringDateTime.Encoder)
                (lastUpdate, x.LastUpdate |> MeteringDateTime.Encoder)
            ]

        let decode (get: Decode.IGetters) : IncludedQuantity =
            {
                Quantity = get.Required.Field quantity Quantity.Decoder
                Created = get.Required.Field created MeteringDateTime.Decoder
                LastUpdate = get.Required.Field lastUpdate MeteringDateTime.Decoder
            }
        
        let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode 

    //module IncludedQuantitySpecification =
    //    let (monthly, annually) = ("monthly", "annually")
    //
    //    let encode (x: IncludedQuantitySpecification) : (string * JsonValue) list =
    //        match x with
    //        | { Monthly = None; Annually = None } -> [ ]
    //        | { Monthly = Some m; Annually = None } -> [ (monthly, m |> Quantity.Encoder) ]
    //        | { Monthly = None; Annually = Some a} -> [ (annually, a |> Quantity.Encoder) ]
    //        | { Monthly = Some m; Annually = Some a } -> [ (monthly, m |> Quantity.Encoder); (annually, a |> Quantity.Encoder) ]
    //  
    //    let decode (get: Decode.IGetters) : IncludedQuantitySpecification =
    //        {
    //            Monthly = get.Optional.Field monthly Quantity.Decoder
    //            Annually = get.Optional.Field annually Quantity.Decoder
    //        }
    //
    //    let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode 

    module MeterValue =
        let (consumed, included) = ("consumed", "included")

        let Encoder (x: MeterValue) : JsonValue =
            match x with
                | ConsumedQuantity q -> [ ( consumed, q |> ConsumedQuantity.Encoder ) ] 
                | IncludedQuantity q -> [ ( included, q |> IncludedQuantity.Encoder ) ]
            |> Encode.object

        let Decoder : Decoder<MeterValue> =
            [
                Decode.field consumed ConsumedQuantity.Decoder |> Decode.map ConsumedQuantity 
                Decode.field included IncludedQuantity.Decoder |> Decode.map IncludedQuantity
            ] |> Decode.oneOf

    module RenewalInterval =
        let Encoder (x: RenewalInterval) =
            match x with
            | Monthly -> nameof(Monthly) |> Encode.string
            | Annually -> nameof(Annually) |> Encode.string
        
        let Decoder : Decoder<RenewalInterval> =
            Decode.string |> Decode.andThen (
               function
               | nameof(Monthly) -> Decode.succeed Monthly
               | nameof(Annually) -> Decode.succeed Annually
               | invalid -> Decode.fail (sprintf "Failed to decode `%s`" invalid))

    module Plan =
        let (planId, billingDimensions) = ("planId", "billingDimensions")

        let encode (x: Plan) : (string * JsonValue) list =
            let a = x.BillingDimensions.value |> Map.toSeq |> Seq.map (fun (k, v) -> (k.value |> Encode.string, v |> Quantity.Encoder))
            [
                (planId, x.PlanId.value |> Encode.string)
                (billingDimensions, x.BillingDimensions.value |> Map.toList |> List.map (fun (k, v) -> (k.value, v |> Quantity.Encoder)) |> Encode.object)
            ]
            
        let decode (get: Decode.IGetters) : Plan =
            let turnKeyIntoDimensionId (k, v) =  (k |> DimensionId.create, v)

            {
                PlanId = (get.Required.Field planId Decode.string) |> PlanId.create
                BillingDimensions = get.Required.Field billingDimensions ((Decode.keyValuePairs Quantity.Decoder) |> Decode.andThen (fun r -> r |> List.map turnKeyIntoDimensionId |> Map.ofList |> BillingDimensions.create |> Decode.succeed))
            }
         
        let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode         

    module InternalResourceId =
        let Encoder =
            Encode.string
                   
        let Decoder : Decoder<InternalResourceId> = 
            Decode.string |> Decode.andThen(InternalResourceId.fromStr >> Decode.succeed)
      
    module InternalUsageEvent =
        let (timestamp, meterName, quantity, properties, internalResourceId) =
            ("timestamp", "meterName", "quantity", "properties", "internalResourceId");

        let encode (x: InternalUsageEvent) : (string * JsonValue) list =
            let EncodeProperties (x: (Map<string, string> option)) = 
                x
                |> Option.defaultWith (fun () -> Map.empty)
                |> Map.toSeq |> Seq.toList<string * string>
                |> List.map (fun (k,v) -> (k, v |> Encode.string))
                |> Encode.object
            
            [
                (internalResourceId, x.InternalResourceId.ToString() |> InternalResourceId.Encoder)
                (timestamp, x.Timestamp |> MeteringDateTime.Encoder)
                (meterName, x.MeterName.value |> Encode.string)
                (quantity, x.Quantity |> Quantity.Encoder)
                (properties, x.Properties |> EncodeProperties)
            ]
        
        let decode (get: Decode.IGetters) : InternalUsageEvent =
            let DecodeProperties : Decoder<Map<string,string>> =
                (Decode.keyValuePairs Decode.string)
                |> Decode.andThen (Map.ofList >> Decode.succeed)

            {
                InternalResourceId = get.Required.Field internalResourceId InternalResourceId.Decoder
                Timestamp = get.Required.Field timestamp MeteringDateTime.Decoder
                MeterName = (get.Required.Field meterName Decode.string) |> ApplicationInternalMeterName.create
                Quantity = get.Required.Field quantity Quantity.Decoder
                Properties = get.Optional.Field properties DecodeProperties
            }
        
        let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode 

    module Subscription =
        let (plan, renewalInterval, subscriptionStart, internalResourceId) = ("plan", "renewalInterval", "subscriptionStart", "scope");

        let encode (x: Subscription) : (string * JsonValue) list =
            [
                (plan, x.Plan |> Plan.Encoder)
                (renewalInterval, x.RenewalInterval |> RenewalInterval.Encoder)
                (subscriptionStart, x.SubscriptionStart |> MeteringDateTime.Encoder)
                (internalResourceId, x.InternalResourceId.ToString() |> InternalResourceId.Encoder)
            ]
        
        let decode (get: Decode.IGetters) : Subscription =
            {
                Plan = get.Required.Field plan Plan.Decoder
                RenewalInterval = get.Required.Field renewalInterval RenewalInterval.Decoder
                SubscriptionStart = get.Required.Field subscriptionStart MeteringDateTime.Decoder
                InternalResourceId = get.Required.Field internalResourceId InternalResourceId.Decoder
            }

        let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode 

    module InternalMetersMapping =
        let Encoder (x: InternalMetersMapping) = 
            x.value
            |> Map.toList
            |> List.map (fun (k, v) -> (k.value, v.value |> Encode.string))
            |> Encode.object

        let Decoder : Decoder<InternalMetersMapping> =
            (Decode.keyValuePairs Decode.string)
            |> Decode.andThen (fun r -> r |> List.map (fun (k, v) -> (k |> ApplicationInternalMeterName.create, v |> DimensionId.create)) |> Map.ofList |> InternalMetersMapping.create |> Decode.succeed)
        
    module CurrentMeterValues = 
        let turnKeyIntoDimensionId (k, v) =  (k |> DimensionId.create, v)

        let Encoder (x: CurrentMeterValues) = 
            x.value
            |> Map.toList
            |> List.map (fun (d, m) -> (d.value, m |> MeterValue.Encoder))
            |> Encode.object

        let Decoder : Decoder<CurrentMeterValues> =
            (Decode.keyValuePairs MeterValue.Decoder) |> Decode.andThen  (fun r -> r |> List.map turnKeyIntoDimensionId |> Map.ofList |> CurrentMeterValues.create |> Decode.succeed)

    module SubscriptionCreationInformation =
        let (subscription, metersMapping) =
            ("subscription", "metersMapping");

        let encode (x: SubscriptionCreationInformation) : (string * JsonValue) list =
            [
                (subscription, x.Subscription |> Subscription.Encoder)
                (metersMapping, x.InternalMetersMapping |> InternalMetersMapping.Encoder)
            ]

        let decode (get: Decode.IGetters) =
            {
                Subscription = get.Required.Field subscription Subscription.Decoder
                InternalMetersMapping = get.Required.Field metersMapping InternalMetersMapping.Decoder
            }
            
        let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode 
    
    module Marketplace =
        module MarketplaceRequest = 
            let (resourceId, quantity, planId, dimensionId, effectiveStartTime) =
                ("resourceId", "quantity", "planId", "dimension", "effectiveStartTime");
            
            let encode (x: MarketplaceRequest) : (string * JsonValue) list =
                [
                    (resourceId, x.ResourceId.ToString() |> InternalResourceId.Encoder)
                    (effectiveStartTime, x.EffectiveStartTime |> MeteringDateTime.Encoder)
                    (planId, x.PlanId.value |> Encode.string)
                    (dimensionId, x.DimensionId.value |> Encode.string)                
                    (quantity, x.Quantity.AsFloat |> Encode.float)                 
                ]

            let decode (get: Decode.IGetters) =
                {
                    ResourceId = get.Required.Field resourceId InternalResourceId.Decoder
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
        
        module ResourceURI =
            let (resourceUri) = ("resourceUri")

            let encode (x: ResourceURI option) = x |> JsonUtil.encodeOption resourceUri Encode.string

            let decode (get: Decode.IGetters) = get.Optional.Field resourceUri Decode.string

        module MarketplaceSubmissionStatus =
            // A composed type, where various types are represented in the same JSON object
            let encode (x: MarketplaceSubmissionStatus) =
                [                    
                    x.Status |> SubmissionStatus.encode
                    x.MessageTime |> MessageTime.encode
                    x.UsageEventID |> UsageEventID.encode
                    x.ResourceURI |> ResourceURI.encode
                ]
                |> List.concat

            let decode (get: Decode.IGetters) =
                {
                    Status = get |> SubmissionStatus.decode
                    MessageTime = get |> MessageTime.decode
                    UsageEventID = get |> UsageEventID.decode
                    ResourceURI = get |> ResourceURI.decode
                }

        module MarketplaceErrorCode =
            let (code, message) = ("code", "message")
            
            let encode (x: MarketplaceErrorCode) =
                [
                    (code, x.Code |> Encode.string)
                    (message, x.Message |> Encode.string)
                ]

            let decode (get: Decode.IGetters) =
                {
                    Code = get.Required.Field code Decode.string
                    Message = get.Required.Field message Decode.string
                }

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

        module MarketplaceArgumentErrorData =
            let encode (x: MarketplaceArgumentErrorData) : (string * JsonValue) list =
                [
                    x.Error |> MarketplaceErrorCode.encode
                    [ "target", x.Target |> Encode.string ]
                ] |> List.concat

            let decode (get: Decode.IGetters) : MarketplaceArgumentErrorData =
                {
                    Error = get |> MarketplaceErrorCode.decode
                    Target = get.Required.Field "target" Decode.string
                }

            let Encoder, Decoder = JsonUtil.createEncoderDecoder encode decode 

        module MarketplaceGenericError =
            let encode (x: MarketplaceGenericError) : (string * JsonValue) list =
                [
                    x.RequestData |> MarketplaceRequest.encode
                    x.Status |> MarketplaceSubmissionStatus.encode
                    [ "error", 
                        [ 
                            x.Error |> MarketplaceArgumentErrorData.encode
                            [ "details", x.ErrorDetails |> List.map MarketplaceArgumentErrorData.Encoder |> Encode.list ]
                        ] |> List.concat |> Encode.object 
                    ]
                ] |> List.concat

            let decode (get: Decode.IGetters) : MarketplaceGenericError =
                {
                    RequestData = get |> MarketplaceRequest.decode
                    Status = get |> MarketplaceSubmissionStatus.decode
                    Error = get.Required.At [ "error" ] MarketplaceArgumentErrorData.Decoder
                    ErrorDetails = get.Required.At [ "error"; "details" ] (Decode.list MarketplaceArgumentErrorData.Decoder)
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

        let (subscription, metersMapping, currentMeters, usageToBeReported, lastProcessedMessage) =
            ("subscription", "metersMapping", "currentMeters", "usageToBeReported", "lastProcessedMessage");

        let encode (x: Meter) : (string * JsonValue) list =
            [
                (subscription, x.Subscription |> Subscription.Encoder)
                (metersMapping, x.InternalMetersMapping |> InternalMetersMapping.Encoder)
                (currentMeters, x.CurrentMeterValues |> CurrentMeterValues.Encoder)
                (usageToBeReported, x.UsageToBeReported |> List.map MarketplaceRequest.Encoder |> Encode.list)
                (lastProcessedMessage, x.LastProcessedMessage |> MessagePosition.Encoder)
            ]
        
        let decode (get: Decode.IGetters) : Meter =
            {
                Subscription = get.Required.Field subscription Subscription.Decoder
                InternalMetersMapping = get.Required.Field metersMapping InternalMetersMapping.Decoder
                CurrentMeterValues = get.Required.Field currentMeters CurrentMeterValues.Decoder
                UsageToBeReported = get.Required.Field usageToBeReported (Decode.list MarketplaceRequest.Decoder)
                LastProcessedMessage = get.Required.Field lastProcessedMessage MessagePosition.Decoder
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
        
        let decode (get: Decode.IGetters) : UnprocessableMessage =
            match (get.Required.Field t Decode.string) with
            | "string" -> (get.Required.Field v Decode.string) |> UnprocessableStringContent
            | "bytes" -> (get.Required.Field v Decode.string) |> Convert.FromBase64String |> UnprocessableByteContent
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
            | SubscriptionDeletion x -> x.ToString() |> encodeKeyValue "SubscriptionDeleted" InternalResourceId.Encoder
            | UsageReported x -> x |> encodeKeyValue "UsageReported" InternalUsageEvent.Encoder
            | UsageSubmittedToAPI x -> x |> encodeKeyValue "UsageSubmittedToAPI" MarketplaceResponse.Encoder
            | UnprocessableMessage x -> x |> encodeKeyValue "UnprocessableMessage" UnprocessableMessage.Encoder
            | RemoveUnprocessedMessages x -> x |> encodeKeyValue "RemoveUnprocessedMessages" RemoveUnprocessedMessages.Encoder

        let decode (get: Decode.IGetters) : MeteringUpdateEvent =
            match (get.Required.Field typeid Decode.string) with
            | "SubscriptionPurchased" -> (get.Required.Field value SubscriptionCreationInformation.Decoder) |> SubscriptionPurchased
            | "SubscriptionDeleted" -> (get.Required.Field value InternalResourceId.Decoder) |> SubscriptionDeletion
            | "UsageReported" -> (get.Required.Field value InternalUsageEvent.Decoder) |> UsageReported
            | "UsageSubmittedToAPI" -> (get.Required.Field value MarketplaceResponse.Decoder) |> UsageSubmittedToAPI
            | "UnprocessableMessage" -> (get.Required.Field value UnprocessableMessage.Decoder) |> UnprocessableMessage
            | "RemoveUnprocessedMessages" -> (get.Required.Field value RemoveUnprocessedMessages.Decoder) |> RemoveUnprocessedMessages
            | _ -> failwith "bad"

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
        let (meters, unprocessable, lastProcessedMessage, plans) = ("meters", "unprocessable", "lastProcessedMessage", "plans")

        let encode (x: MeterCollection) : (string * JsonValue) list =
            [
                (meters, x.MeterCollection |> Map.toList |> List.map (fun (k, v) -> (k.ToString(), v |> Meter.Encoder)) |> Encode.object)                
                (unprocessable, x.UnprocessableMessages |> List.map EventHubEvent_MeteringUpdateEvent.Encoder |> Encode.list)
                //(plans, x.Plans |> Plans.Encoder)
            ]
            |> (fun l -> 
                match x.LastUpdate with
                | None -> l
                | Some lastUpdate -> (lastProcessedMessage, lastUpdate |> MessagePosition.Encoder) :: l)
        
        let decode (get: Decode.IGetters) : MeterCollection =
            let turnKeyIntoSubscriptionType (k, v) =
                (k |> InternalResourceId.fromStr, v)

            {
                MeterCollection = get.Required.Field meters ((Decode.keyValuePairs Meter.Decoder) |> Decode.andThen (fun r -> r |> List.map turnKeyIntoSubscriptionType |> Map.ofList |> Decode.succeed))
                UnprocessableMessages = get.Required.Field unprocessable (Decode.list EventHubEvent_MeteringUpdateEvent.Decoder)
                LastUpdate = get.Optional.Field lastProcessedMessage MessagePosition.Decoder
                //Plans = get.Required.Field plans Plans.Decoder
            }

    let enrich x =
        x
        |> Extra.withUInt64
        |> Extra.withInt64
        |> Extra.withCustom Marketplace.MarketplaceRequest.Encoder Marketplace.MarketplaceRequest.Decoder
        |> JsonUtil.withCustom Marketplace.MarketplaceBatchRequest.encode Marketplace.MarketplaceBatchRequest.decode
        |> JsonUtil.withCustom Marketplace.SubmissionStatus.encode Marketplace.SubmissionStatus.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceSubmissionStatus.encode Marketplace.MarketplaceSubmissionStatus.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceErrorCode.encode Marketplace.MarketplaceErrorCode.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceSuccessResponse.encode Marketplace.MarketplaceSuccessResponse.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceErrorDuplicate.encode Marketplace.MarketplaceErrorDuplicate.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceArgumentErrorData.encode Marketplace.MarketplaceArgumentErrorData.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceGenericError.encode Marketplace.MarketplaceGenericError.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceSubmissionError.encode Marketplace.MarketplaceSubmissionError.decode
        |> JsonUtil.withCustom Marketplace.MarketplaceBatchResponseDTO.encode Marketplace.MarketplaceBatchResponseDTO.decode
        |> Extra.withCustom Marketplace.MarketplaceSubmissionResult.Encoder Marketplace.MarketplaceSubmissionResult.Decoder
        |> Extra.withCustom Quantity.Encoder Quantity.Decoder
        |> Extra.withCustom MeteringDateTime.Encoder MeteringDateTime.Decoder
        |> Extra.withCustom MessagePosition.Encoder MessagePosition.Decoder
        //|> Extra.withCustom IncludedQuantitySpecification.Encoder IncludedQuantitySpecification.Decoder
        |> Extra.withCustom ConsumedQuantity.Encoder ConsumedQuantity.Decoder
        |> Extra.withCustom IncludedQuantity.Encoder IncludedQuantity.Decoder
        |> Extra.withCustom MeterValue.Encoder MeterValue.Decoder
        |> Extra.withCustom RenewalInterval.Encoder RenewalInterval.Decoder
        |> Extra.withCustom Plan.Encoder Plan.Decoder
        |> Extra.withCustom InternalUsageEvent.Encoder InternalUsageEvent.Decoder
        |> Extra.withCustom Subscription.Encoder Subscription.Decoder
        |> Extra.withCustom InternalMetersMapping.Encoder InternalMetersMapping.Decoder
        |> Extra.withCustom CurrentMeterValues.Encoder CurrentMeterValues.Decoder
        |> Extra.withCustom SubscriptionCreationInformation.Encoder SubscriptionCreationInformation.Decoder
        |> Extra.withCustom Meter.Encoder Meter.Decoder
        |> Extra.withCustom MeteringUpdateEvent.Encoder MeteringUpdateEvent.Decoder
        |> Extra.withCustom EventHubEvent_MeteringUpdateEvent.Encoder EventHubEvent_MeteringUpdateEvent.Decoder
        |> JsonUtil.withCustom MeterCollection.encode MeterCollection.decode

    let enriched = Extra.empty |> enrich

    let toStr ([<Optional; DefaultParameterValue(0)>] space: int) o = Encode.Auto.toString(space, o, extra = enriched)
        
    let fromStr<'T> json = 
        match Decode.Auto.fromString<'T>(json, extra = enriched) with
        | Ok r -> r
        | Result.Error e -> 
            failwith e
    
    let fromStr2<'T> json = 
        Decode.Auto.fromString<'T>(json, extra = enriched)