namespace Metering.Types

module Json =
    open System
    open NodaTime
    open Thoth.Json.Net
    open NodaTime.Text
    open System.Globalization

    module NodaTime =
        let writeLocalDate = LocalDatePattern.Create("yyyy-MM-dd", CultureInfo.InvariantCulture).Format >> Encode.string
        let readLocalDate = Decode.string |> Decode.map(fun value -> LocalDatePattern.Create("yyyy-MM-dd", CultureInfo.InvariantCulture).Parse(value).Value)
        let writeLocalTime = LocalTimePattern.Create("HH:mm", CultureInfo.InvariantCulture).Format >> Encode.string
        let readLocalTime = Decode.string |> Decode.map(fun value -> LocalTimePattern.Create("HH:mm", CultureInfo.InvariantCulture).Parse(value).Value)
    
    module ConsumedQuantity =
        let Encoder (x: ConsumedQuantity) : JsonValue =
            [
                ("consumedQuantity", x.Amount |> Encode.uint64)
            ]
            |> Encode.object 

        let Decoder : Decoder<ConsumedQuantity> =
            Decode.object (fun fields -> {
                Amount = fields.Required.At [ "consumedQuantity" ] Decode.uint64
            })

    module IncludedQuantity =
        let Encoder (x: IncludedQuantity) =
            match x with
            | { Monthly = None; Annually = None } -> [ ]
            | { Monthly = Some m; Annually = None } ->
                [ 
                    ("monthly", m |> Encode.uint64) 
                ]
            | { Monthly = None; Annually = Some a} -> 
                [ 
                    ("annually", a |> Encode.uint64)
                ]
            | { Monthly = Some m; Annually = Some a } ->
                [
                    ("monthly", m |> Encode.uint64)
                    ("annually", a |> Encode.uint64)
                ]
            |> Encode.object

        let Decoder : Decoder<IncludedQuantity> =
            Decode.object (fun fields -> {
                Monthly = fields.Optional.At [ "monthly" ] Decode.uint64
                Annually = fields.Optional.At [ "annually" ] Decode.uint64
            })
        
    module MeterValue =
        let (consumed, included, meter) = ("consumed", "included", "meter")

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

    module XX =
        let Encoder (x: XX) =
            [ 
                ("name", x.Name |> Encode.string ) 
                ("interval", x.RenewalInterval |> RenewalInterval.Encoder) 
            ]
            |> Encode.object 

        let Decoder : Decoder<XX> =
            Decode.object (fun fields -> {
                Name = fields.Required.At [ "name" ] Decode.string
                RenewalInterval = fields.Required.At [ "interval" ] RenewalInterval.Decoder
            })

    module MarketPlaceAPIJSON =
        open MarketPlaceAPI

        module BillingDimension =
            let (dimensionId, dimensionName, unitOfMeasure, includedQuantity) = ("dimension", "name", "unitOfMeasure", "includedQuantity");
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
            let (planId, billingDimensions) = ("planId", "billingDimensions");

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
                    (quantity, x.Quantity |> Encode.uint64)
                    (dimensionId, x.DimensionId |> Encode.string)
                    (effectiveStartTime, x.EffectiveStartTime |> Encode.datetime)
                    (planId, x.PlanId |> Encode.string)
                ]
                |> Encode.object 
            
            let Decoder : Decoder<MeteredBillingUsageEvent> =
                Decode.object (fun fields -> {
                    ResourceID = fields.Required.At [ resourceID ] Decode.string
                    Quantity = fields.Required.At [ quantity ] Decode.uint64
                    DimensionId = fields.Required.At [ dimensionId ] Decode.string
                    EffectiveStartTime = fields.Required.At [ effectiveStartTime ] Decode.datetime
                    PlanId = fields.Required.At [ planId ] Decode.string
                })
            //let EncoderBatch (x: MeteredBillingUsageEventBatch) : JsonValue = x |> List.map Encoder |> Encode.list
            //let DecoderBatch : Decoder<MeteredBillingUsageEventBatch> = Decode.list Decoder

    open MarketPlaceAPIJSON
    
    let enrich x =
        x
        |> Extra.withUInt64
        |> Extra.withCustom NodaTime.writeLocalDate NodaTime.readLocalDate
        |> Extra.withCustom NodaTime.writeLocalTime NodaTime.readLocalTime
        |> Extra.withCustom ConsumedQuantity.Encoder ConsumedQuantity.Decoder
        |> Extra.withCustom IncludedQuantity.Encoder IncludedQuantity.Decoder
        |> Extra.withCustom MeterValue.Encoder MeterValue.Decoder
        |> Extra.withCustom RenewalInterval.Encoder RenewalInterval.Decoder
        |> Extra.withCustom BillingDimension.Encoder BillingDimension.Decoder
        |> Extra.withCustom MeteredBillingUsageEvent.Encoder MeteredBillingUsageEvent.Decoder
        |> Extra.withCustom Plan.Encoder Plan.Decoder
        |> Extra.withCustom XX.Encoder XX.Decoder