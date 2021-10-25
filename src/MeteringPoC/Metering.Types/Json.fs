namespace Metering.Types

module Json =
    open System.Globalization
    open Thoth.Json.Net
    open NodaTime.Text

    module NodaTime =
        let writeLocalDate = LocalDatePattern.Create("yyyy-MM-dd", CultureInfo.InvariantCulture).Format >> Encode.string
        let readLocalDate = Decode.string |> Decode.map(fun value -> LocalDatePattern.Create("yyyy-MM-dd", CultureInfo.InvariantCulture).Parse(value).Value)
        let writeLocalTime = LocalTimePattern.Create("HH:mm", CultureInfo.InvariantCulture).Format >> Encode.string
        let readLocalTime = Decode.string |> Decode.map(fun value -> LocalTimePattern.Create("HH:mm", CultureInfo.InvariantCulture).Parse(value).Value)
    
    module Quantity =
        let Encoder (x: Quantity) : JsonValue = x |> Encode.uint64
        let Decoder : Decoder<Quantity> = Decode.uint64

    module EventHubJSON =
        open Metering.Types.EventHub

        let (partitionId, sequenceNumber, partitionTimestamp) = 
            ("partitionId", "sequenceNumber", "partitionTimestamp")

        let Encoder (x: MessagePosition) : JsonValue =
            [
                (partitionId, x.PartitionID |> Encode.string)
                (sequenceNumber, x.SequenceNumber |> Encode.uint64)
                (partitionTimestamp, x.PartitionTimestamp |> Encode.datetime)
            ]
            |> Encode.object 

        let Decoder : Decoder<MessagePosition> =
            Decode.object (fun fields -> {
                PartitionID = fields.Required.At [ partitionId ] Decode.string
                SequenceNumber = fields.Required.At [ sequenceNumber ] Decode.uint64
                PartitionTimestamp = fields.Required.At [ partitionTimestamp ] Decode.datetime
            })

    module ConsumedQuantity =
        let (consumedQuantity) = 
            ("consumedQuantity")

        let Encoder (x: ConsumedQuantity) : JsonValue =
            [
                (consumedQuantity, x.Amount |> Quantity.Encoder)
            ]
            |> Encode.object 

        let Decoder : Decoder<ConsumedQuantity> =
            Decode.object (fun fields -> {
                Amount = fields.Required.At [ consumedQuantity ] Quantity.Decoder
            })

    module IncludedQuantity =
        let (monthly, annually) =
            ("monthly", "annually")

        let Encoder (x: IncludedQuantity) =
            match x with
            | { Monthly = None; Annually = None } -> [ ]
            | { Monthly = Some m; Annually = None } -> [ (monthly, m |> Quantity.Encoder) ]
            | { Monthly = None; Annually = Some a} -> [ (annually, a |> Quantity.Encoder) ]
            | { Monthly = Some m; Annually = Some a } -> [ (monthly, m |> Quantity.Encoder); (annually, a |> Quantity.Encoder) ]
            |> Encode.object

        let Decoder : Decoder<IncludedQuantity> =
            Decode.object (fun fields -> {
                Monthly = fields.Optional.At [ monthly ] Quantity.Decoder
                Annually = fields.Optional.At [ annually ] Quantity.Decoder
            })
        
    module MeterValue =
        let (consumed, included, meter) =
            ("consumed", "included", "meter")

        let Encoder (x: MeterValue) : JsonValue =
            match x with
            | ConsumedQuantity q -> [ ( consumed, q |> ConsumedQuantity.Encoder ) ] 
            | IncludedQuantity q -> [ ( included, q |> IncludedQuantity.Encoder ) ]
            |> Encode.object
            |> (fun x ->  [ (meter, x ) ] )
            |> Encode.object
        
        let Decoder : Decoder<MeterValue> =
            let consumedDecoder = Decode.field consumed ConsumedQuantity.Decoder |> Decode.map ConsumedQuantity 
            let includedDecoder = Decode.field included IncludedQuantity.Decoder |> Decode.map IncludedQuantity
            Decode.field meter ( Decode.oneOf [ consumedDecoder; includedDecoder ] )

    module RenewalInterval =
        let Encoder (x: RenewalInterval) =
            match x with
            | Monthly -> "Monthly" |> Encode.string
            | Annually -> "Annually" |> Encode.string
        
        let Decoder : Decoder<RenewalInterval> =
            Decode.string |> Decode.andThen (
               function
               | "Monthly" -> Decode.succeed Monthly
               | "Annually" -> Decode.succeed Annually
               | invalid -> Decode.fail (sprintf "Failed to decode `%s`" invalid))

    module MarketPlaceAPIJSON =
        open MarketPlaceAPI

        module BillingDimension =
            let (dimensionId, dimensionName, unitOfMeasure, includedQuantity) =
                ("dimension", "name", "unitOfMeasure", "includedQuantity");

            let Encoder (x: BillingDimension) : JsonValue =
                [
                    (dimensionId, x.DimensionId |> Encode.string)
                    (dimensionName, x.DimensionName |> Encode.string)
                    (unitOfMeasure, x.UnitOfMeasure |> Encode.string)
                    (includedQuantity, x.IncludedQuantity |> IncludedQuantity.Encoder)
                ]
                |> Encode.object 

            let Decoder : Decoder<BillingDimension> =
                Decode.object (fun fields -> {
                    DimensionId = fields.Required.At [ dimensionId ] Decode.string
                    DimensionName = fields.Required.At [ dimensionName ] Decode.string
                    UnitOfMeasure = fields.Required.At [ unitOfMeasure ] Decode.string
                    IncludedQuantity = fields.Required.At [ includedQuantity ] IncludedQuantity.Decoder
                })
        
        module Plan =
            let (planId, billingDimensions) =
                ("planId", "billingDimensions");

            let Encoder (x: Plan) : JsonValue =
                [
                    (planId, x.PlanId |> Encode.string)
                    (billingDimensions, x.BillingDimensions |> Seq.map BillingDimension.Encoder |> Encode.seq)
                ]
                |> Encode.object 
            
            let Decoder : Decoder<Plan> =
                Decode.object (fun fields -> {
                    PlanId = fields.Required.At [ planId ] Decode.string
                    BillingDimensions = (fields.Required.At [ billingDimensions ] (Decode.list BillingDimension.Decoder)) |> List.toSeq
                })

        module MeteredBillingUsageEvent = 
            let (resourceID, quantity, dimensionId, effectiveStartTime, planId) = 
                ("resourceID", "quantity", "dimensionId", "effectiveStartTime", "planId");

            let Encoder (x: MeteredBillingUsageEvent) : JsonValue =
                [
                    (resourceID, x.ResourceID |> Encode.string)
                    (quantity, x.Quantity |> Quantity.Encoder)
                    (dimensionId, x.DimensionId |> Encode.string)
                    (effectiveStartTime, x.EffectiveStartTime |> Encode.datetime)
                    (planId, x.PlanId |> Encode.string)
                ]
                |> Encode.object 
            
            let Decoder : Decoder<MeteredBillingUsageEvent> =
                Decode.object (fun fields -> {
                    ResourceID = fields.Required.At [ resourceID ] Decode.string
                    Quantity = fields.Required.At [ quantity ] Quantity.Decoder
                    DimensionId = fields.Required.At [ dimensionId ] Decode.string
                    EffectiveStartTime = fields.Required.At [ effectiveStartTime ] Decode.datetime
                    PlanId = fields.Required.At [ planId ] Decode.string
                })

    module InternalUsageEvent =
        let (timestamp, meterName, quantity, properties) =
            ("timestamp", "meterName", "quantity", "properties");

        let EncodeMap (x: (Map<string,string> option)) = 
            x
            |> Option.defaultWith (fun () -> Map.empty)
            |> Map.toSeq |> Seq.toList
            |> List.map (fun (k,v) -> (k, v |> Encode.string))
            |> Encode.object

        let DecodeMap : Decoder<Map<string,string>> =
            (Decode.keyValuePairs Decode.string)
            |> Decode.andThen (fun r -> r |> Map.ofList |> Decode.succeed)

        let Encoder (x: InternalUsageEvent) : JsonValue =
            [
                (timestamp, x.Timestamp |> Encode.datetime)
                (meterName, x.MeterName |> Encode.string)
                (quantity, x.Quantity |> Quantity.Encoder)
                (properties, x.Properties |> EncodeMap)
            ]
            |> Encode.object 

        let Decoder : Decoder<InternalUsageEvent> =
            Decode.object (fun fields -> {
                Timestamp = fields.Required.At [ timestamp ] Decode.datetime
                MeterName = fields.Required.At [ meterName ] Decode.string
                Quantity = fields.Required.At [ quantity ] Quantity.Decoder
                Properties = fields.Optional.At [ properties ] DecodeMap
            })

    module Subscription =
        let (planId, renewalInterval, subscriptionStart) =
            ("plan", "renewalInterval", "subscriptionStart");

        let Encoder (x: Subscription) : JsonValue =
            [
                (planId, x.PlanId |> Encode.string)
                (renewalInterval, x.RenewalInterval |> RenewalInterval.Encoder)
                (subscriptionStart, x.SubscriptionStart |> NodaTime.writeLocalDate)
            ]
            |> Encode.object 

        let Decoder : Decoder<Subscription> =
            Decode.object (fun fields -> {
                PlanId = fields.Required.At [ planId ] Decode.string
                RenewalInterval = fields.Required.At [ renewalInterval ] RenewalInterval.Decoder
                SubscriptionStart = fields.Required.At [ subscriptionStart ] NodaTime.readLocalDate
            })

    module PlanDimension =
        let (planId, dimensionId) =
            ("plan", "dimension");

        let Encoder (x: PlanDimension) : JsonValue =
            [
                (planId, x.PlanId |> Encode.string)
                (dimensionId, x.DimensionId |> Encode.string)
            ]
            |> Encode.object 
        
        let Decoder : Decoder<PlanDimension> =
            Decode.object (fun fields -> {
                PlanId = fields.Required.At [ planId ] Decode.string
                DimensionId = fields.Required.At [ dimensionId ] Decode.string
            })

    module InternalMetersMapping =
        let Encoder (x: InternalMetersMapping) = 
            x
            |> Map.toSeq |> Seq.toList
            |> List.map (fun (k,v) -> (k, v |> PlanDimension.Encoder))
            |> Encode.object

        let Decoder : Decoder<InternalMetersMapping> =
            (Decode.keyValuePairs PlanDimension.Decoder)
            |> Decode.andThen (fun r -> r |> Map.ofList |> Decode.succeed)
        
    module CurrentMeterValues = 

        let Encoder (x: CurrentMeterValues) = 
            x
            |> Map.toSeq |> Seq.toList
            |> List.map (fun (planDim, v) -> 
                [
                    ("planId", planDim.PlanId |> Encode.string)
                    ("dimensionId", planDim.DimensionId |> Encode.string)
                    ("meterValue", v |> MeterValue.Encoder)
                ]
                |> Encode.object)
            |> Encode.list

        let Decoder : Decoder<CurrentMeterValues> =            
            Decode.list (Decode.object (fun get -> 
                let k = {
                    PlanId = get.Required.Field "planId" Decode.string  
                    DimensionId = get.Required.Field "dimensionId" Decode.string  
                }
                let v = get.Required.Field "meterValue" MeterValue.Decoder  
                (k,v)
            ))
            |> Decode.andThen  (fun r -> r |> Map.ofList |> Decode.succeed)

    module MeteringAPIUsageEventDefinition = 
        let (resourceId, quantity, planDimension, effectiveStartTime) =
            ("resourceId", "quantity", "planDimension", "effectiveStartTime");

        let Encoder (x: MeteringAPIUsageEventDefinition) : JsonValue =
            [
                (resourceId, x.ResourceId |> Encode.string)
                (quantity, x.Quantity |> Encode.float)
                (planDimension, x.PlanDimension |> PlanDimension.Encoder)
                (effectiveStartTime, x.EffectiveStartTime |> Encode.datetime)
            ]
            |> Encode.object 
        
        let Decoder : Decoder<MeteringAPIUsageEventDefinition> =
            Decode.object (fun fields -> {
                ResourceId = fields.Required.At [ resourceId ] Decode.string
                Quantity = fields.Required.At [ quantity ] Decode.float
                PlanDimension = fields.Required.At [ planDimension ] PlanDimension.Decoder
                EffectiveStartTime = fields.Required.At [ effectiveStartTime ] Decode.datetime
            })
    
    module SubscriptionCreationInformation =
        open MarketPlaceAPIJSON

        let (plans, initialPurchase, metersMapping) =
            ("plans", "initialPurchase", "metersMapping");

        let Encoder (x: SubscriptionCreationInformation) : JsonValue =
            [
                (plans, x.Plans |> List.map Plan.Encoder |> Encode.list)
                (initialPurchase, x.InitialPurchase |> Subscription.Encoder)
                (metersMapping, x.InternalMetersMapping |> InternalMetersMapping.Encoder)
            ]
            |> Encode.object 

        let Decoder : Decoder<SubscriptionCreationInformation> =
            Decode.object (fun fields -> {
                Plans = fields.Required.At [ plans ] (Decode.list Plan.Decoder)
                InitialPurchase = fields.Required.At [ initialPurchase ] Subscription.Decoder
                InternalMetersMapping = fields.Required.At [ metersMapping ] InternalMetersMapping.Decoder
            })

    module MeteringState =
        open MarketPlaceAPIJSON
        
        let (plans, initialPurchase, metersMapping, currentMeters, usageToBeReported, lastProcessedMessage) =
            ("plans", "initialPurchase", "metersMapping", "currentMeters", "usageToBeReported", "lastProcessedMessage");

        let Encoder (x: MeteringState) : JsonValue =
            [
                (plans, x.Plans |> List.map Plan.Encoder |> Encode.list)
                (initialPurchase, x.InitialPurchase |> Subscription.Encoder)
                (metersMapping, x.InternalMetersMapping |> InternalMetersMapping.Encoder)
                (currentMeters, x.CurrentMeterValues |> CurrentMeterValues.Encoder)
                (usageToBeReported, x.UsageToBeReported |> List.map MeteringAPIUsageEventDefinition.Encoder |> Encode.list)
                (lastProcessedMessage, x.LastProcessedMessage |> EventHubJSON.Encoder)
            ]
            |> Encode.object 

        let Decoder : Decoder<MeteringState> =
            Decode.object (fun fields -> {
                Plans = fields.Required.At [ plans ] (Decode.list Plan.Decoder)
                InitialPurchase = fields.Required.At [ initialPurchase ] Subscription.Decoder
                InternalMetersMapping = fields.Required.At [ metersMapping ] InternalMetersMapping.Decoder
                CurrentMeterValues = fields.Required.At [ currentMeters ] CurrentMeterValues.Decoder
                UsageToBeReported = fields.Required.At [ usageToBeReported ] (Decode.list MeteringAPIUsageEventDefinition.Decoder)
                LastProcessedMessage = fields.Required.At [ lastProcessedMessage ] EventHubJSON.Decoder
            })
       
    module MeteringUpdateEvent =
        let (typeid, value) =
            ("type", "value");

        let Encoder (x: MeteringUpdateEvent) : JsonValue =
            match x with
            | SubscriptionPurchased sub -> 
                [
                     (typeid, "subscriptionPurchased" |> Encode.string)
                     (value, sub |> SubscriptionCreationInformation.Encoder)
                ]
            | UsageReported usage ->
                [
                     (typeid, "usage" |> Encode.string)
                     (value, usage |> InternalUsageEvent.Encoder)
                ]
            | UsageSubmittedToAPI usage -> raise (new System.NotSupportedException "Currently this feedback loop must only be internally")
            |> Encode.object 
            
        let Decoder : Decoder<MeteringUpdateEvent> =
            Decode.object (fun get ->
                match get.Required.Field typeid Decode.string with
                | "subscriptionPurchased" -> (get.Required.Field value SubscriptionCreationInformation.Decoder) |> SubscriptionPurchased
                | "usage" -> (get.Required.Field value InternalUsageEvent.Decoder) |> UsageReported
                | invalidType  -> failwithf "`%s` is not a valid type" invalidType
            )

    open MarketPlaceAPIJSON
    
    let enrich x =
        x
        |> Extra.withUInt64
        |> Extra.withCustom Quantity.Encoder Quantity.Decoder
        |> Extra.withCustom NodaTime.writeLocalDate NodaTime.readLocalDate
        |> Extra.withCustom NodaTime.writeLocalTime NodaTime.readLocalTime
        |> Extra.withCustom EventHubJSON.Encoder EventHubJSON.Decoder
        |> Extra.withCustom ConsumedQuantity.Encoder ConsumedQuantity.Decoder
        |> Extra.withCustom IncludedQuantity.Encoder IncludedQuantity.Decoder
        |> Extra.withCustom MeterValue.Encoder MeterValue.Decoder
        |> Extra.withCustom RenewalInterval.Encoder RenewalInterval.Decoder
        |> Extra.withCustom BillingDimension.Encoder BillingDimension.Decoder
        |> Extra.withCustom Plan.Encoder Plan.Decoder
        |> Extra.withCustom MeteredBillingUsageEvent.Encoder MeteredBillingUsageEvent.Decoder
        |> Extra.withCustom InternalUsageEvent.Encoder InternalUsageEvent.Decoder
        |> Extra.withCustom Subscription.Encoder Subscription.Decoder
        |> Extra.withCustom PlanDimension.Encoder PlanDimension.Decoder
        |> Extra.withCustom InternalMetersMapping.Encoder InternalMetersMapping.Decoder
        |> Extra.withCustom CurrentMeterValues.Encoder CurrentMeterValues.Decoder
        |> Extra.withCustom MeteringAPIUsageEventDefinition.Encoder MeteringAPIUsageEventDefinition.Decoder
        |> Extra.withCustom SubscriptionCreationInformation.Encoder SubscriptionCreationInformation.Decoder
        |> Extra.withCustom MeteringState.Encoder MeteringState.Decoder
        |> Extra.withCustom MeteringUpdateEvent.Encoder MeteringUpdateEvent.Decoder
        