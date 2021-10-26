namespace Metering.Types

open System
open NodaTime
open Metering.Types.EventHub

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
    type MeteredBillingUsageEvent =
        { ResourceID: string // unique identifier of the resource against which usage is emitted. 
          Quantity: Quantity // how many units were consumed for the date and hour specified in effectiveStartTime, must be greater than 0, can be integer or float value
          DimensionId: DimensionId // custom dimension identifier
          EffectiveStartTime: MeteringDateTime // time in UTC when the usage event occurred, from now and until 24 hours back
          PlanId: PlanId } // id of the plan purchased for the offer

    type MeteredBillingUsageEventBatch = 
        MeteredBillingUsageEvent list
 
open MarketPlaceAPI

type ApplicationInternalMeterName = string // A meter name used between app and aggregator

type InternalUsageEvent = // From app to aggregator
    { Timestamp: MeteringDateTime  // timestamp from the sending app
      MeterName: ApplicationInternalMeterName
      Quantity: Quantity
      Properties: Map<string, string> option}

type BusinessError =
    | DayBeforeSubscription

type Subscription = // When a certain plan was purchased
    { PlanId: PlanId
      RenewalInterval: RenewalInterval 
      SubscriptionStart: MeteringDateTime }

type BillingPeriod = // Each time the subscription is renewed, a new billing period start
    { Start: MeteringDateTime
      End: MeteringDateTime
      Index: uint }

type PlanDimension =
    { PlanId: PlanId
      DimensionId: DimensionId }

type InternalMetersMapping = // The mapping table used by the aggregator to translate an ApplicationInternalMeterName to the plan and dimension configured in Azure marketplace
    Map<ApplicationInternalMeterName, PlanDimension>

type CurrentMeterValues = // Collects all meters per internal metering event type
    Map<PlanDimension, MeterValue> 

type MeteringAPIUsageEventDefinition = // From aggregator to metering API
    { ResourceId: string 
      Quantity: double 
      PlanDimension: PlanDimension
      EffectiveStartTime: MeteringDateTime }

type SubscriptionCreationInformation = // This event needs to be injected at first
    { Plans: Plan list // The list of all plans. Certainly not needed in the aggregator?
      InitialPurchase: Subscription // The purchase information of the subscription
      InternalMetersMapping: InternalMetersMapping } // The table mapping app-internal meter names to 'proper' ones for marketplace
    
type MeteringState =
    { Plans: Plan list // The list of all plans. Certainly not needed in the aggregator?
      InitialPurchase: Subscription // The purchase information of the subscription
      InternalMetersMapping: InternalMetersMapping // The table mapping app-internal meter names to 'proper' ones for marketplace
      CurrentMeterValues: CurrentMeterValues // The current meter values in the aggregator
      UsageToBeReported: MeteringAPIUsageEventDefinition list // a list of usage elements which haven't yet been reported to the metering API
      LastProcessedMessage: MessagePosition } // Pending HTTP calls to the marketplace 
        
module MeteringState =
    let setCurrentMeterValues x s = { s with CurrentMeterValues = x }
    let setLastProcessedMessage x s = { s with LastProcessedMessage = x }
    let addUsageToBeReported x s = { s with UsageToBeReported = (x :: s.UsageToBeReported) }
    let removeUsageToBeReported x s = { s with UsageToBeReported = (s.UsageToBeReported |> List.filter (fun e -> e <> x)) }

type UpdateOutOfOrderError =
    { DataWatermark: MessagePosition 
      UpdateWatermark: MessagePosition }

type BusinessDataUpdateError = 
    | UpdateOutOfOrderError of UpdateOutOfOrderError
    | SnapshotDownloadError of Exception

type UsageSubmissionResult = // Once the metering API was called, either the metering submission successfully got through, or not (in which case we need to know which values haven't been submitted)
    Result<MeteringAPIUsageEventDefinition, Exception * MeteringAPIUsageEventDefinition>

type MeteringUpdateEvent =
    | SubscriptionPurchased of SubscriptionCreationInformation
    | UsageReported of InternalUsageEvent // app -> aggregator
    | UsageSubmittedToAPI of UsageSubmissionResult // aggregator -> aggregator

type MeteringEvent =
    { MeteringUpdateEvent: MeteringUpdateEvent
      MessagePosition: MessagePosition }
   
type BusinessLogic = // The business logic takes the current state, and a command to be applied, and returns new state
    MeteringState option -> MeteringEvent -> MeteringState option 

type SubmitMeteringAPIUsageEvent =
    MeteringAPIUsageEventDefinition -> System.Threading.Tasks.Task
