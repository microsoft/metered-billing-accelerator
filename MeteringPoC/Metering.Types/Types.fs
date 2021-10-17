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
          Quantity: Quantity // how many units were consumed for the date and hour specified in effectiveStartTime, must be greater than 0, can be integer or float value
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
