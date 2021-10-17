namespace Metering.Types

open System
open NodaTime
open Thoth.Json.Net
open Metering.Types.EventHub
open NodaTime.Text
open System.Globalization

type IntOrFloat =
    | Int of uint64 // ? :-)
    | Float of double

type Quantity = uint64

type ConsumedQuantity = 
    { Amount: Quantity }

type IncludedQuantity = 
    { Monthly: Quantity option
      Annually: Quantity option }

type MeterValue =
    | ConsumedQuantity of ConsumedQuantity
    | IncludedQuantity of IncludedQuantity

type RenewalInterval =
    | Monthly
    | Annually

type XX =
    { Name: string
      RenewalInterval: RenewalInterval }

module Json =
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
                    ( "monthly", m |> Encode.uint64) 
                ]
            | { Monthly = None; Annually = Some a} -> 
                [ 
                    ( "annually", a |> Encode.uint64)
                ]
            | { Monthly = Some m; Annually = Some a } ->
                [
                    ( "monthly", m |> Encode.uint64)
                    ( "annually", a |> Encode.uint64)
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

    let enrich x =
        x
        |> Extra.withCustom NodaTime.writeLocalDate NodaTime.readLocalDate
        |> Extra.withCustom NodaTime.writeLocalTime NodaTime.readLocalTime
        |> Extra.withCustom ConsumedQuantity.Encoder ConsumedQuantity.Decoder
        |> Extra.withCustom IncludedQuantity.Encoder IncludedQuantity.Decoder
        |> Extra.withCustom MeterValue.Encoder MeterValue.Decoder
        |> Extra.withCustom RenewalInterval.Encoder RenewalInterval.Decoder
        |> Extra.withCustom XX.Encoder XX.Decoder




        
module MarketPlaceAPI =
    type PlanId = string
    type DimensionId = string
    type UnitOfMeasure = string

    // https://docs.microsoft.com/en-us/azure/marketplace/azure-app-metered-billing#billing-dimensions
    type BillingDimension =
        { DimensionId: DimensionId
          DimensionName: string 
          UnitOfMeasure: UnitOfMeasure
          IncludedQuantity: IncludedQuantity }
      
    type Plan =
        { PlanId: PlanId
          BillingDimensions: BillingDimension seq }

    // https://docs.microsoft.com/en-us/azure/marketplace/marketplace-metering-service-apis#metered-billing-single-usage-event
    type MeteredBillingSingleUsageEvent =
        { ResourceID: string // unique identifier of the resource against which usage is emitted. 
          Quantity: IntOrFloat // how many units were consumed for the date and hour specified in effectiveStartTime, must be greater than 0, can be integer or float value
          DimensionId: DimensionId // custom dimension identifier
          EffectiveStartTime: DateTime // time in UTC when the usage event occurred, from now and until 24 hours back
          PlanId: PlanId } // id of the plan purchased for the offer

    type MeteredBillingBatchUsageEvent = 
        MeteredBillingSingleUsageEvent seq

open MarketPlaceAPI

type ApplicationInternalMeterName = string // A meter name used between app and aggregator

type InternalUsageEvent = // From app to aggregator
    { Timestamp: DateTime
      MeterName: ApplicationInternalMeterName
      Quantity: Quantity
      Properties: Map<string, string> option}

type BusinessError =
    | DayBeforeSubscription

type Subscription = // When a certain plan was purchased
    { PlanId: PlanId
      RenewalInterval: RenewalInterval 
      SubscriptionStart: LocalDate }

type BillingPeriod = // Each time the subscription is renewed, a new billing period start
    { FirstDay: LocalDate
      LastDay: LocalDate
      Index: uint }

type PlanDimension =
    { PlanId: PlanId
      DimensionId: DimensionId }

type InternalMetersMapping = // The mapping table used by the aggregator to translate an ApplicationInternalMeterName to the plan and dimension configured in Azure marketplace
    Map<ApplicationInternalMeterName, PlanDimension>

type CurrentMeterValues = // Collects all meters per internal metering event type
    Map<ApplicationInternalMeterName, MeterValue> 

type MeteringAPIUsageEventDefinition = // From aggregator to metering API
    { ResourceId: string 
      Quantity: double 
      PlanDimension: PlanDimension
      EffectiveStartTime: DateTime }
    
type MeteringState =
    { Plans: Plan seq // The list of all plans. Certainly not needed in the aggregator?
      InitialPurchase: Subscription // The purchase information of the subscription
      InternalMetersMapping: InternalMetersMapping // The table mapping app-internal meter names to 'proper' ones for marketplace
      CurrentMeterValues: CurrentMeterValues // The current meter values in the aggregator
      UsageToBeReported: MeteringAPIUsageEventDefinition list // a list of usage elements which haven't yet been reported to the metering API
      LastProcessedMessage: MessagePosition } // Pending HTTP calls to the marketplace API
