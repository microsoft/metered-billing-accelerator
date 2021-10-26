namespace Metering.Types

module Json =
    open Thoth.Json.Net
    open NodaTime.Text

    module MeteringDateTime =
        let private makeEncoder<'T> (pattern : IPattern<'T>) : Encoder<'T> = pattern.Format >> Encode.string
        let private makeDecoder<'T> (pattern : IPattern<'T>) : Decoder<'T> = 
            Decode.string |> Decode.andThen (fun v ->
                let x = pattern.Parse(v)
                if x.Success
                then Decode.succeed x.Value
                else Decode.fail (sprintf "Failed to decode `%s`" v)
        )

        //let instantPattern = InstantPattern.CreateWithInvariantCulture("yyyy-MM-dd--HH-mm-ss-FFF")
        //let private localDatePattern = LocalDatePattern.CreateWithInvariantCulture("yyyy-MM-dd")        
        //let private localTimePattern = LocalTimePattern.CreateWithInvariantCulture("HH:mm")
        //let encodeLocalDate = makeEncoder localDatePattern
        //let decodeLocalDate = makeDecoder localDatePattern
        //let encodeLocalTime = makeEncoder localTimePattern
        //let decodeLocalTime = makeDecoder localTimePattern
        
        // Use the first pattern as default, therefore the `|> List.head`
        let Encoder : Encoder<MeteringDateTime> = MeteringDateTime.meteringDateTimePatterns |> List.head |> makeEncoder

        // This supports decoding of multiple formats on how Date and Time could be represented, therefore the `|> Decode.oneOf`
        let Decoder : Decoder<MeteringDateTime> = MeteringDateTime.meteringDateTimePatterns |> List.map makeDecoder |> Decode.oneOf

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
                (partitionTimestamp, x.PartitionTimestamp |> MeteringDateTime.Encoder)
            ]
            |> Encode.object 

        let Decoder : Decoder<MessagePosition> =
            Decode.object (fun get -> {
                PartitionID = get.Required.Field partitionId Decode.string
                SequenceNumber = get.Required.Field sequenceNumber Decode.uint64
                PartitionTimestamp = get.Required.Field partitionTimestamp MeteringDateTime.Decoder
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
            Decode.object (fun get -> {
                Amount = get.Required.Field consumedQuantity Quantity.Decoder
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
            let consumedDecoder = Decode.field consumed ConsumedQuantity.Decoder |> Decode.map ConsumedQuantity 
            let includedDecoder = Decode.field included IncludedQuantity.Decoder |> Decode.map IncludedQuantity
            Decode.oneOf [ consumedDecoder; includedDecoder ]

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
                Decode.object (fun get -> {
                    DimensionId = get.Required.Field dimensionId Decode.string
                    DimensionName = get.Required.Field dimensionName Decode.string
                    UnitOfMeasure = get.Required.Field unitOfMeasure Decode.string
                    IncludedQuantity = get.Required.Field includedQuantity IncludedQuantity.Decoder
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
                Decode.object (fun get -> {
                    PlanId = get.Required.Field planId Decode.string
                    BillingDimensions = (get.Required.Field billingDimensions (Decode.list BillingDimension.Decoder)) |> List.toSeq
                })

        module MeteredBillingUsageEvent = 
            let (resourceID, quantity, dimensionId, effectiveStartTime, planId) = 
                ("resourceID", "quantity", "dimensionId", "effectiveStartTime", "planId");

            let Encoder (x: MeteredBillingUsageEvent) : JsonValue =
                [
                    (resourceID, x.ResourceID |> Encode.string)
                    (quantity, x.Quantity |> Quantity.Encoder)
                    (dimensionId, x.DimensionId |> Encode.string)
                    (effectiveStartTime, x.EffectiveStartTime |> MeteringDateTime.Encoder)
                    (planId, x.PlanId |> Encode.string)
                ]
                |> Encode.object 
            
            let Decoder : Decoder<MeteredBillingUsageEvent> =
                Decode.object (fun get -> {
                    ResourceID = get.Required.Field resourceID Decode.string
                    Quantity = get.Required.Field quantity Quantity.Decoder
                    DimensionId = get.Required.Field dimensionId Decode.string
                    EffectiveStartTime = get.Required.Field effectiveStartTime MeteringDateTime.Decoder
                    PlanId = get.Required.Field planId Decode.string
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
                (timestamp, x.Timestamp |> MeteringDateTime.Encoder)
                (meterName, x.MeterName |> Encode.string)
                (quantity, x.Quantity |> Quantity.Encoder)
                (properties, x.Properties |> EncodeMap)
            ]
            |> Encode.object 

        let Decoder : Decoder<InternalUsageEvent> =
            Decode.object (fun get -> {
                Timestamp = get.Required.Field timestamp MeteringDateTime.Decoder
                MeterName = get.Required.Field meterName Decode.string
                Quantity = get.Required.Field quantity Quantity.Decoder
                Properties = get.Optional.Field properties DecodeMap
            })

    module Subscription =
        let (planId, renewalInterval, subscriptionStart) =
            ("plan", "renewalInterval", "subscriptionStart");

        let Encoder (x: Subscription) : JsonValue =
            [
                (planId, x.PlanId |> Encode.string)
                (renewalInterval, x.RenewalInterval |> RenewalInterval.Encoder)
                (subscriptionStart, x.SubscriptionStart |> MeteringDateTime.Encoder)
            ]
            |> Encode.object 

        let Decoder : Decoder<Subscription> =
            Decode.object (fun get -> {
                PlanId = get.Required.Field planId Decode.string
                RenewalInterval = get.Required.Field renewalInterval RenewalInterval.Decoder
                SubscriptionStart = get.Required.Field subscriptionStart MeteringDateTime.Decoder
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
            Decode.object (fun get -> {
                PlanId = get.Required.Field planId Decode.string
                DimensionId = get.Required.Field dimensionId Decode.string
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
                (effectiveStartTime, x.EffectiveStartTime |> MeteringDateTime.Encoder)
            ]
            |> Encode.object 
        
        let Decoder : Decoder<MeteringAPIUsageEventDefinition> =
            Decode.object (fun get -> {
                ResourceId = get.Required.Field resourceId Decode.string
                Quantity = get.Required.Field quantity Decode.float
                PlanDimension = get.Required.Field planDimension PlanDimension.Decoder
                EffectiveStartTime = get.Required.Field effectiveStartTime MeteringDateTime.Decoder
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
            Decode.object (fun get -> {
                Plans = get.Required.Field plans (Decode.list Plan.Decoder)
                InitialPurchase = get.Required.Field initialPurchase Subscription.Decoder
                InternalMetersMapping = get.Required.Field metersMapping InternalMetersMapping.Decoder
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
            Decode.object (fun get -> {
                Plans = get.Required.Field plans (Decode.list Plan.Decoder)
                InitialPurchase = get.Required.Field initialPurchase Subscription.Decoder
                InternalMetersMapping = get.Required.Field metersMapping InternalMetersMapping.Decoder
                CurrentMeterValues = get.Required.Field currentMeters CurrentMeterValues.Decoder
                UsageToBeReported = get.Required.Field usageToBeReported (Decode.list MeteringAPIUsageEventDefinition.Decoder)
                LastProcessedMessage = get.Required.Field lastProcessedMessage EventHubJSON.Decoder
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
        |> Extra.withCustom MeteringDateTime.Encoder MeteringDateTime.Decoder
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
        