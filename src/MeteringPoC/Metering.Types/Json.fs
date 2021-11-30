namespace Metering.Types

module Json =
    open System
    open Thoth.Json.Net
    open Newtonsoft.Json.Linq
    open NodaTime.Text
    open System.Runtime.InteropServices
    
    module MeteringDateTime =
        let private makeEncoder<'T> (pattern : IPattern<'T>) : Encoder<'T> = pattern.Format >> Encode.string
        let private makeDecoder<'T> (pattern : IPattern<'T>) : Decoder<'T> = 
            Decode.string |> Decode.andThen (fun v ->
                let x = pattern.Parse(v)
                if x.Success
                then Decode.succeed x.Value
                else Decode.fail (sprintf "Failed to decode `%s`" v))
        
        // Use the first pattern as default, therefore the `|> List.head`
        let Encoder : Encoder<MeteringDateTime> = MeteringDateTime.meteringDateTimePatterns |> List.head |> makeEncoder

        // This supports decoding of multiple formats on how Date and Time could be represented, therefore the `|> Decode.oneOf`
        let Decoder : Decoder<MeteringDateTime> = MeteringDateTime.meteringDateTimePatterns |> List.map makeDecoder |> Decode.oneOf

    module Quantity =
        let Encoder (x: Quantity) : JsonValue = 
            match x with
            | MeteringInt i -> i |> Encode.uint64
            | MeteringFloat f -> f |> Encode.decimal
            | Infinite -> "Infinite" |> Encode.string
            
        let Decoder : Decoder<Quantity> = 
            let decodeInfinite s = 
                match s with
                | "Infinite" -> Infinite |> Decode.succeed
                | invalid -> (sprintf "Failed to decode `%s`" invalid) |> Decode.fail

            [ 
                Decode.uint64 |> Decode.andThen(Quantity.createInt >> Decode.succeed)
                Decode.decimal |> Decode.andThen(Quantity.createFloat >> Decode.succeed)
                Decode.string |> Decode.andThen(decodeInfinite)
            ] |> Decode.oneOf

    module EventHubJSON =
        open Metering.Types.EventHub

        let (partitionId, sequenceNumber, partitionTimestamp, offset) = 
            ("partitionId", "sequenceNumber", "partitionTimestamp", "offset")

        let Encoder (x: MessagePosition) : JsonValue =
            [
                (partitionId, x.PartitionID |> PartitionID.value |> Encode.string)
                (sequenceNumber, x.SequenceNumber |> Encode.int64)
                (offset, x.Offset |> Encode.int64)
                (partitionTimestamp, x.PartitionTimestamp |> MeteringDateTime.Encoder)
            ]
            |> Encode.object 

        let Decoder : Decoder<MessagePosition> =
            Decode.object (fun get -> {
                PartitionID = (get.Required.Field partitionId Decode.string) |> PartitionID.create
                SequenceNumber = get.Required.Field sequenceNumber Decode.int64
                Offset = get.Required.Field offset Decode.int64
                PartitionTimestamp = get.Required.Field partitionTimestamp MeteringDateTime.Decoder                
            })

    module ConsumedQuantity =
        let (consumedQuantity, created, lastUpdate) = 
            ("consumedQuantity", "created", "lastUpdate")

        let Encoder (x: ConsumedQuantity) : JsonValue =
            [
                (consumedQuantity, x.Amount |> Quantity.Encoder)
                (created, x.Created |> MeteringDateTime.Encoder)
                (lastUpdate, x.LastUpdate |> MeteringDateTime.Encoder)
            ]
            |> Encode.object 

        let Decoder : Decoder<ConsumedQuantity> =
            Decode.object (fun get -> {
                Amount = get.Required.Field consumedQuantity Quantity.Decoder
                Created = get.Required.Field created MeteringDateTime.Decoder
                LastUpdate = get.Required.Field lastUpdate MeteringDateTime.Decoder
            })

    module IncludedQuantity =
        let (monthly, annually, created, lastUpdate) =
            ("monthly", "annually", "created", "lastUpdate")

        let Encoder (x: IncludedQuantity) =
            let ts = [ 
                (created, x.Created |> MeteringDateTime.Encoder)
                (lastUpdate, x.LastUpdate |> MeteringDateTime.Encoder)
            ]

            match x with
                | { Monthly = None; Annually = None } -> ts
                | { Monthly = Some m; Annually = None } -> ts |> List.append [ (monthly, m |> Quantity.Encoder) ]
                | { Monthly = None; Annually = Some a} -> ts |> List.append [ (annually, a |> Quantity.Encoder) ]
                | { Monthly = Some m; Annually = Some a } -> ts |> List.append [ (monthly, m |> Quantity.Encoder); (annually, a |> Quantity.Encoder) ]
            |> Encode.object

        let Decoder : Decoder<IncludedQuantity> =
            Decode.object (fun get -> {
                Monthly = get.Optional.Field monthly Quantity.Decoder
                Annually = get.Optional.Field annually Quantity.Decoder
                Created = get.Required.Field created MeteringDateTime.Decoder
                LastUpdate = get.Required.Field lastUpdate MeteringDateTime.Decoder
            })

    module IncludedQuantitySpecification =
        let (monthly, annually) =
            ("monthly", "annually")

        let Encoder (x: IncludedQuantitySpecification) =
            match x with
                | { Monthly = None; Annually = None } -> [ ]
                | { Monthly = Some m; Annually = None } -> [ (monthly, m |> Quantity.Encoder) ]
                | { Monthly = None; Annually = Some a} -> [ (annually, a |> Quantity.Encoder) ]
                | { Monthly = Some m; Annually = Some a } -> [ (monthly, m |> Quantity.Encoder); (annually, a |> Quantity.Encoder) ]
            |> Encode.object

        let Decoder : Decoder<IncludedQuantitySpecification> =
            Decode.object (fun get -> {
                Monthly = get.Optional.Field monthly Quantity.Decoder
                Annually = get.Optional.Field annually Quantity.Decoder
            })
        
    module MeterValue =
        let (consumed, included) =
            ("consumed", "included")

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

    module BillingDimension =
        let (dimensionId, name, unitOfMeasure, includedQuantity) =
            ("dimension", "name", "unitOfMeasure", "includedQuantity");

        let Encoder (x: BillingDimension) : JsonValue =
            [
                (dimensionId, x.DimensionId |> DimensionId.value |> Encode.string)
                (name, x.DimensionName |> Encode.string)
                (unitOfMeasure, x.UnitOfMeasure |> UnitOfMeasure.value |> Encode.string)
                (includedQuantity, x.IncludedQuantity |> IncludedQuantitySpecification.Encoder)
            ] |> Encode.object 

        let Decoder : Decoder<BillingDimension> =
            Decode.object (fun get -> {
                DimensionId = (get.Required.Field dimensionId Decode.string) |> DimensionId.create
                DimensionName = get.Required.Field name Decode.string
                UnitOfMeasure = (get.Required.Field unitOfMeasure Decode.string) |> UnitOfMeasure.create
                IncludedQuantity = get.Required.Field includedQuantity IncludedQuantitySpecification.Decoder
            })
        
    module Plan =
        let (planId, billingDimensions) =
            ("planId", "billingDimensions");

        let Encoder (x: Plan) : JsonValue =
            [
                (planId, x.PlanId |> PlanId.value |> Encode.string)
                (billingDimensions, x.BillingDimensions |> Seq.map BillingDimension.Encoder |> Encode.seq)
            ] |> Encode.object 
            
        let Decoder : Decoder<Plan> =
            Decode.object (fun get -> {
                PlanId = (get.Required.Field planId Decode.string) |> PlanId.create
                BillingDimensions = (get.Required.Field billingDimensions (Decode.list BillingDimension.Decoder)) |> List.toSeq
            })

    module MarketplaceResourceID =
        let Encoder (x: MarketplaceResourceID) : JsonValue =
            match x with
                | ManagedAppResourceGroupID x -> x |> ManagedAppResourceGroupID.value
                | SaaSSubscriptionID x ->  x |> SaaSSubscriptionID.value
            |> Encode.string
            
        let Decoder : Decoder<MarketplaceResourceID> = 
            Decode.string |> Decode.andThen (fun v -> 
                if v.StartsWith("/subscriptions")
                    then v |> ManagedAppResourceGroupID.create |> ManagedAppResourceGroupID 
                    else v |> SaaSSubscriptionID.create |> SaaSSubscriptionID
                |> Decode.succeed)

    module MeteredBillingUsageEvent = 
        let (resourceID, quantity, dimensionId, effectiveStartTime, planId) = 
            ("resourceID", "quantity", "dimensionId", "effectiveStartTime", "planId");

        let Encoder (x: MeteredBillingUsageEvent) : JsonValue =
            [
                (resourceID, x.ResourceID |> MarketplaceResourceID.Encoder)
                (quantity, x.Quantity |> Quantity.Encoder)
                (dimensionId, x.DimensionId |> DimensionId.value |> Encode.string)
                (effectiveStartTime, x.EffectiveStartTime |> MeteringDateTime.Encoder)
                (planId, x.PlanId |> PlanId.value |> Encode.string)
            ] |> Encode.object 
            
        let Decoder : Decoder<MeteredBillingUsageEvent> =
            Decode.object (fun get -> {
                ResourceID = get.Required.Field resourceID MarketplaceResourceID.Decoder
                Quantity = get.Required.Field quantity Quantity.Decoder
                DimensionId = (get.Required.Field dimensionId Decode.string) |> DimensionId.create
                EffectiveStartTime = get.Required.Field effectiveStartTime MeteringDateTime.Decoder
                PlanId = (get.Required.Field planId Decode.string) |> PlanId.create
            })

    module InternalResourceId =        
        let Encoder =
            InternalResourceId.toStr >> Encode.string
                   
        let Decoder : Decoder<InternalResourceId> = 
            Decode.string |> Decode.andThen(InternalResourceId.fromStr >> Decode.succeed)
      
    module InternalUsageEvent =
        let (timestamp, meterName, quantity, properties, internalResourceId) =
            ("timestamp", "meterName", "quantity", "properties", "internalResourceId");

        let Encoder (x: InternalUsageEvent) : JsonValue =
            let EncodeProperties (x: (Map<string, string> option)) = 
                x
                |> Option.defaultWith (fun () -> Map.empty)
                |> Map.toSeq |> Seq.toList<string * string>
                |> List.map (fun (k,v) -> (k, v |> Encode.string))
                |> Encode.object
            
            [
                (internalResourceId, x.InternalResourceId |> InternalResourceId.Encoder)
                (timestamp, x.Timestamp |> MeteringDateTime.Encoder)
                (meterName, x.MeterName |> ApplicationInternalMeterName.value |> Encode.string)
                (quantity, x.Quantity |> Quantity.Encoder)
                (properties, x.Properties |> EncodeProperties)
            ] |> Encode.object 

        let Decoder : Decoder<InternalUsageEvent> =
            let DecodeProperties : Decoder<Map<string,string>> =
                (Decode.keyValuePairs Decode.string)
                |> Decode.andThen (Map.ofList >> Decode.succeed)

            Decode.object (fun get -> {
                InternalResourceId = get.Required.Field internalResourceId InternalResourceId.Decoder
                Timestamp = get.Required.Field timestamp MeteringDateTime.Decoder
                MeterName = (get.Required.Field meterName Decode.string) |> ApplicationInternalMeterName.create
                Quantity = get.Required.Field quantity Quantity.Decoder
                Properties = get.Optional.Field properties DecodeProperties
            })

    module Subscription =
        let (plan, renewalInterval, subscriptionStart, internalResourceId) =
            ("plan", "renewalInterval", "subscriptionStart", "scope");

        let Encoder (x: Subscription) : JsonValue =
            [
                (plan, x.Plan |> Plan.Encoder)
                (renewalInterval, x.RenewalInterval |> RenewalInterval.Encoder)
                (subscriptionStart, x.SubscriptionStart |> MeteringDateTime.Encoder)
                (internalResourceId, x.InternalResourceId |> InternalResourceId.Encoder)
            ] |> Encode.object 

        let Decoder : Decoder<Subscription> =
            Decode.object (fun get -> {
                Plan = get.Required.Field plan Plan.Decoder
                RenewalInterval = get.Required.Field renewalInterval RenewalInterval.Decoder
                SubscriptionStart = get.Required.Field subscriptionStart MeteringDateTime.Decoder
                InternalResourceId = get.Required.Field internalResourceId InternalResourceId.Decoder
            })

    module InternalMetersMapping =
        let Encoder (x: InternalMetersMapping) = 
            x
            |> InternalMetersMapping.value |> Map.toSeq |> Seq.toList
            |> List.map (fun (k, v) -> (k |> ApplicationInternalMeterName.value, v |> DimensionId.value |> Encode.string))
            |> Encode.object

        let Decoder : Decoder<InternalMetersMapping> =
            (Decode.keyValuePairs Decode.string)
            |> Decode.andThen (fun r -> r |> List.map (fun (k, v) -> (k |> ApplicationInternalMeterName.create, v |> DimensionId.create)) |> Map.ofList |> InternalMetersMapping.create |> Decode.succeed)
        
    module CurrentMeterValues = 
        let (dimensionId, meterValue) =
            ("dimensionId", "meterValue")

        let Encoder (x: CurrentMeterValues) = 
            x
            |> Map.toSeq |> Seq.toList
            |> List.map (fun (d, m) -> 
                [
                    (dimensionId, d |> DimensionId.value |> Encode.string)
                    (meterValue, m |> MeterValue.Encoder)
                ]
                |> Encode.object)
            |> Encode.list

        let Decoder : Decoder<CurrentMeterValues> =            
            Decode.list (Decode.object (fun get -> 
                let dimensionId = get.Required.Field dimensionId Decode.string  
                let meterValue = get.Required.Field meterValue MeterValue.Decoder  
                (dimensionId, meterValue)
            ))
            |> Decode.andThen  (fun r -> r |> List.map(fun (k, v) -> (k |> DimensionId.create, v)) |> Map.ofList |> Decode.succeed)

    module MeteringAPIUsageEventDefinition = 
        let (resourceId, quantity, planId, dimensionId, effectiveStartTime) =
            ("resourceId", "quantity", "plan", "dimension", "effectiveStartTime");

        let Encoder (x: MeteringAPIUsageEventDefinition) : JsonValue =
            [
                (quantity, x.Quantity |> Encode.decimal) 
                (planId, x.PlanId |> PlanId.value |> Encode.string)
                (dimensionId, x.DimensionId |> DimensionId.value |> Encode.string)
                (effectiveStartTime, x.EffectiveStartTime |> MeteringDateTime.Encoder)
                (resourceId, x.ResourceId |> InternalResourceId.Encoder)
            ] |> Encode.object 
        
        let Decoder : Decoder<MeteringAPIUsageEventDefinition> =
            Decode.object (fun get -> {
                Quantity = get.Required.Field quantity Decode.decimal
                PlanId = (get.Required.Field planId Decode.string) |> PlanId.create
                DimensionId = (get.Required.Field dimensionId Decode.string) |> DimensionId.create
                EffectiveStartTime = get.Required.Field effectiveStartTime MeteringDateTime.Decoder
                ResourceId = get.Required.Field resourceId InternalResourceId.Decoder
            })
    
    module SubscriptionCreationInformation =
        let (subscription, metersMapping) =
            ("subscription", "metersMapping");

        let Encoder (x: SubscriptionCreationInformation) : JsonValue =
            [
                (subscription, x.Subscription |> Subscription.Encoder)
                (metersMapping, x.InternalMetersMapping |> InternalMetersMapping.Encoder)
            ] |> Encode.object 

        let Decoder : Decoder<SubscriptionCreationInformation> =
            Decode.object (fun get -> {
                Subscription = get.Required.Field subscription Subscription.Decoder
                InternalMetersMapping = get.Required.Field metersMapping InternalMetersMapping.Decoder
            })

    module Meter =
        let (subscription, metersMapping, currentMeters, usageToBeReported, lastProcessedMessage) =
            ("subscription", "metersMapping", "currentMeters", "usageToBeReported", "lastProcessedMessage");

        let Encoder (x: Meter) : JsonValue =
            [
                (subscription, x.Subscription |> Subscription.Encoder)
                (metersMapping, x.InternalMetersMapping |> InternalMetersMapping.Encoder)
                (currentMeters, x.CurrentMeterValues |> CurrentMeterValues.Encoder)
                (usageToBeReported, x.UsageToBeReported |> List.map MeteringAPIUsageEventDefinition.Encoder |> Encode.list)
                (lastProcessedMessage, x.LastProcessedMessage |> EventHubJSON.Encoder)
            ] |> Encode.object 

        let Decoder : Decoder<Meter> =
            Decode.object (fun get -> {
                Subscription = get.Required.Field subscription Subscription.Decoder
                InternalMetersMapping = get.Required.Field metersMapping InternalMetersMapping.Decoder
                CurrentMeterValues = get.Required.Field currentMeters CurrentMeterValues.Decoder
                UsageToBeReported = get.Required.Field usageToBeReported (Decode.list MeteringAPIUsageEventDefinition.Decoder)
                LastProcessedMessage = get.Required.Field lastProcessedMessage EventHubJSON.Decoder
            })

    module MeterCollection =
        let Encoder (x: MeterCollection) = 
            x
            |> MeterCollection.value
            |> Map.toSeq |> Seq.toList
            |> List.map (fun (k, v) -> (k |> InternalResourceId.toStr, v |> Meter.Encoder))
            |> Encode.object

        let Decoder : Decoder<MeterCollection> =
            let turnKeyIntoSubscriptionType (k, v) =
                (k |> InternalResourceId.fromStr, v)

            (Decode.keyValuePairs Meter.Decoder)
            |> Decode.andThen (fun r -> r |> List.map turnKeyIntoSubscriptionType  |> Map.ofList |> MeterCollection.create |> Decode.succeed)

    
    module MarketplaceSubmissionAcceptedResponse =
        let (usageEventId, status, messageTime, resourceId, resourceUri, quantity, dimensionId, effectiveStartTime, planId) =
            ("usageEventId", "status", "messageTime", "resourceId", "resourceUri", "quantity", "dimension", "effectiveStartTime", "planId");

        let Encoder (x: MarketplaceSubmissionAcceptedResponse) : JsonValue =
            [
                (usageEventId, x.UsageEventId |> Encode.string)
                (status, x.Status |> Encode.string)
                (messageTime, x.MessageTime |> MeteringDateTime.Encoder)
                (resourceId, x.ResourceId |> Encode.string)
                (resourceUri, x.ResourceURI |> Encode.string)
                (quantity, x.Quantity |> Quantity.Encoder)
                (dimensionId, x.DimensionId |> DimensionId.value |> Encode.string)
                (effectiveStartTime, x.EffectiveStartTime |> MeteringDateTime.Encoder)
                (planId, x.PlanId |> PlanId.value |> Encode.string)
            ] |> Encode.object 

        let Decoder : Decoder<MarketplaceSubmissionAcceptedResponse> =
            Decode.object (fun get -> {
                UsageEventId = get.Required.Field usageEventId Decode.string
                Status =  get.Required.Field status Decode.string
                MessageTime = get.Required.Field messageTime MeteringDateTime.Decoder
                ResourceId = get.Required.Field resourceId Decode.string
                ResourceURI = get.Required.Field resourceUri Decode.string
                Quantity = get.Required.Field quantity Quantity.Decoder
                DimensionId = (get.Required.Field dimensionId Decode.string) |> DimensionId.create
                EffectiveStartTime = get.Required.Field effectiveStartTime MeteringDateTime.Decoder
                PlanId = (get.Required.Field planId Decode.string) |> PlanId.create
            })

    module MarketplaceSubmissionError =
        let (error, body) =
            ("error", "body");

        let Encoder (x: MarketplaceSubmissionError) : JsonValue =
            // TODO... Would be great to have JSON object structure in the body, instead of a string containing JSON... JsonValue.Parse 
            let encodeBody : (string -> JToken) = Encode.string 
            
            match x with
            | Duplicate json -> (nameof(Duplicate), json)
            | BadResourceId json -> (nameof(BadResourceId), json)
            | InvalidEffectiveStartTime json -> (nameof(InvalidEffectiveStartTime), json)
            | CommunicationsProblem json -> (nameof(CommunicationsProblem), json)
            |> (fun (e, b) ->
                [
                    (error, e |> Encode.string)
                    (body, b |> encodeBody)
                ] |> Encode.object 
            )

        let Decoder : Decoder<MarketplaceSubmissionError> =
            Decode.object (fun get -> 
                let errName = get.Required.Field error Decode.string
                let json = get.Required.Field body Decode.string
                

                let r = 
                    match errName with
                    | nameof(Duplicate) -> json |> Duplicate
                    | nameof(BadResourceId) -> json |> BadResourceId
                    | nameof(InvalidEffectiveStartTime) -> json |> InvalidEffectiveStartTime
                    | nameof(CommunicationsProblem) -> json |> CommunicationsProblem
                    | unknown -> failwith $"{nameof(MarketplaceSubmissionError)} '{unknown}' is unknown"
                r
            )

    module AzureHttpResponseHeaders =
        let (msrequestid, mscorrelationid) =
            ("xMsRequestId", "xMsCorrelationId");

        let Encoder (x: AzureHttpResponseHeaders) : JsonValue =
            [
                (msrequestid, x.RequestID |> Encode.string)
                (mscorrelationid, x.CorrelationID |> Encode.string)
            ] |> Encode.object 

        let Decoder : Decoder<AzureHttpResponseHeaders> =
            Decode.object (fun get -> {
                RequestID = get.Required.Field msrequestid Decode.string
                CorrelationID =  get.Required.Field mscorrelationid Decode.string
            })

    module MarketplaceSubmissionResult =
        let (payload, result, httpHeaders) =
            ("payload", "result", "httpHeaders");

        let ResultEncoder (x: Result<MarketplaceSubmissionAcceptedResponse, MarketplaceSubmissionError>) : JsonValue =
            match x with
            | Ok x -> x |> MarketplaceSubmissionAcceptedResponse.Encoder
            | Error x -> x |> MarketplaceSubmissionError.Encoder

        let ResultDecoder : Decoder<Result<MarketplaceSubmissionAcceptedResponse, MarketplaceSubmissionError>> =
            [ 
                MarketplaceSubmissionAcceptedResponse.Decoder |> Decode.andThen(Ok >> Decode.succeed)
                MarketplaceSubmissionError.Decoder |> Decode.andThen(Error >> Decode.succeed)
            ] |> Decode.oneOf

        let Encoder (x: MarketplaceSubmissionResult) : JsonValue =
            [
                (payload, x.Payload |> MeteringAPIUsageEventDefinition.Encoder)
                (httpHeaders, x.Headers |> AzureHttpResponseHeaders.Encoder)
                (result, x.Result |> ResultEncoder)                
            ] |> Encode.object 

        let Decoder : Decoder<MarketplaceSubmissionResult> =
            Decode.object (fun get -> {
                Payload = get.Required.Field payload MeteringAPIUsageEventDefinition.Decoder
                Headers = get.Required.Field httpHeaders AzureHttpResponseHeaders.Decoder
                Result = get.Required.Field result ResultDecoder
            })
            
    module MeteringUpdateEvent =
        let (typeid, value) =
            ("type", "value");

        let Encoder (x: MeteringUpdateEvent) : JsonValue =
            match x with
            | SubscriptionPurchased sub -> 
                [
                     (typeid, nameof(SubscriptionPurchased) |> Encode.string)
                     (value, sub |> SubscriptionCreationInformation.Encoder)
                ]
            | UsageReported usage ->
                [
                     (typeid, nameof(UsageReported) |> Encode.string)
                     (value, usage |> InternalUsageEvent.Encoder)
                ]
            | UsageSubmittedToAPI usage -> 
                [
                    (typeid, nameof(UsageSubmittedToAPI) |> Encode.string)
                    (value, usage |> MarketplaceSubmissionResult.Encoder)
                ]
            | AggregatorBooted -> raise <| new NotSupportedException "Currently this feedback loop must only be internally"
            |> Encode.object 
            
        let Decoder : Decoder<MeteringUpdateEvent> =
            Decode.object (fun get ->                
                match (get.Required.Field typeid Decode.string) with
                | nameof(SubscriptionPurchased) -> (get.Required.Field value SubscriptionCreationInformation.Decoder) |> SubscriptionPurchased
                | nameof(UsageReported) -> (get.Required.Field value InternalUsageEvent.Decoder) |> UsageReported
                | nameof(UsageSubmittedToAPI) -> (get.Required.Field value MarketplaceSubmissionResult.Decoder) |> UsageSubmittedToAPI
                | invalidType  -> failwithf "`%s` is not a valid type" invalidType
            )

    let enrich x =
        x
        |> Extra.withUInt64
        |> Extra.withInt64
        |> Extra.withCustom Quantity.Encoder Quantity.Decoder
        |> Extra.withCustom MeteringDateTime.Encoder MeteringDateTime.Decoder
        |> Extra.withCustom EventHubJSON.Encoder EventHubJSON.Decoder
        |> Extra.withCustom IncludedQuantitySpecification.Encoder IncludedQuantitySpecification.Decoder
        |> Extra.withCustom ConsumedQuantity.Encoder ConsumedQuantity.Decoder
        |> Extra.withCustom IncludedQuantity.Encoder IncludedQuantity.Decoder
        |> Extra.withCustom MeterValue.Encoder MeterValue.Decoder
        |> Extra.withCustom RenewalInterval.Encoder RenewalInterval.Decoder
        |> Extra.withCustom BillingDimension.Encoder BillingDimension.Decoder
        |> Extra.withCustom MarketplaceSubmissionResult.Encoder MarketplaceSubmissionResult.Decoder
        |> Extra.withCustom MarketplaceSubmissionAcceptedResponse.Encoder MarketplaceSubmissionAcceptedResponse.Decoder
        |> Extra.withCustom Plan.Encoder Plan.Decoder
        |> Extra.withCustom MeteredBillingUsageEvent.Encoder MeteredBillingUsageEvent.Decoder
        |> Extra.withCustom InternalUsageEvent.Encoder InternalUsageEvent.Decoder
        |> Extra.withCustom Subscription.Encoder Subscription.Decoder
        |> Extra.withCustom InternalMetersMapping.Encoder InternalMetersMapping.Decoder
        |> Extra.withCustom CurrentMeterValues.Encoder CurrentMeterValues.Decoder
        |> Extra.withCustom MeteringAPIUsageEventDefinition.Encoder MeteringAPIUsageEventDefinition.Decoder
        |> Extra.withCustom SubscriptionCreationInformation.Encoder SubscriptionCreationInformation.Decoder
        |> Extra.withCustom Meter.Encoder Meter.Decoder
        |> Extra.withCustom MeteringUpdateEvent.Encoder MeteringUpdateEvent.Decoder
        |> Extra.withCustom MeterCollection.Encoder MeterCollection.Decoder

    let enriched = Extra.empty |> enrich

    let toStr ([<Optional; DefaultParameterValue(0)>] space: int) o = Encode.Auto.toString(space, o, extra = enriched)
        
    let fromStr<'T> json = 
        match Decode.Auto.fromString<'T>(json, extra = enriched) with
        | Ok r -> r
        | Result.Error e -> failwith e
    