namespace Metering.Types

open System
open NodaTime
open Metering.Types.EventHub

type IntOrFloat =
    | Int of uint64 // ? :-)
    | Float of double

type Quantity = uint64

type ConsumedQuantity = 
    { Amount: Quantity
      Created: MeteringDateTime 
      LastUpdate: MeteringDateTime }

type IncludedQuantitySpecification = 
    { Monthly: Quantity option
      Annually: Quantity option }

type IncludedQuantity = 
    { Monthly: Quantity option
      Annually: Quantity option
      Created: MeteringDateTime 
      LastUpdate: MeteringDateTime }

type MeterValue =
    | ConsumedQuantity of ConsumedQuantity
    | IncludedQuantity of IncludedQuantity

module IncludedQuantity =
    let private decrease (v: Quantity) (q: Quantity option) : Quantity option =
        match q with
        | Some a -> Some (a - v)
        | None -> failwith "mustnot"

    let decreaseAnnually now amount q = { q with Annually = q.Annually |> decrease amount ; LastUpdate = now }
    let decreaseMonthly now amount q = { q with Monthly = q.Monthly |> decrease amount ; LastUpdate = now }
    let removeAnnually now q = { q with Annually = None ; LastUpdate = now }
    let removeMonthly now q = { q with Monthly = None ; LastUpdate = now }

module ConsumedQuantity =
    let create now amount = { Amount = amount; Created = now ; LastUpdate = now }
    let increaseConsumption now amount q = { q with Amount = q.Amount + amount ; LastUpdate = now }

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
          IncludedQuantity: IncludedQuantitySpecification }
      
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
    { Plan: Plan
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
    Map<ApplicationInternalMeterName, DimensionId>

type CurrentMeterValues = // Collects all meters per internal metering event type
    Map<DimensionId, MeterValue> 

type MeteringAPIUsageEventDefinition = // From aggregator to metering API
    { ResourceId: string 
      Quantity: double 
      PlanDimension: PlanDimension
      EffectiveStartTime: MeteringDateTime }

/// Event representing the creation of a subscription. 
type SubscriptionCreationInformation =
    { Subscription: Subscription // The purchase information of the subscription
      InternalMetersMapping: InternalMetersMapping } // The table mapping app-internal meter names to 'proper' ones for marketplace
    
type MeteringState =
    { Subscription: Subscription // The purchase information of the subscription
      InternalMetersMapping: InternalMetersMapping // The table mapping app-internal meter names to 'proper' ones for marketplace
      CurrentMeterValues: CurrentMeterValues // The current meter values in the aggregator
      UsageToBeReported: MeteringAPIUsageEventDefinition list // a list of usage elements which haven't yet been reported to the metering API
      LastProcessedMessage: MessagePosition } // Pending HTTP calls to the marketplace 
        
module MeteringState =
    let initial : (MeteringState option) = None

    let setCurrentMeterValues x s = { s with CurrentMeterValues = x }
    let applyToCurrentMeterValue f s = { s with CurrentMeterValues = (f s.CurrentMeterValues) }
    let setLastProcessedMessage x s = { s with LastProcessedMessage = x }
    let addUsageToBeReported x s = { s with UsageToBeReported = (x :: s.UsageToBeReported) }
    let addUsagesToBeReported x s = { s with UsageToBeReported = List.concat [ x; s.UsageToBeReported ] }
    
    /// Removes the item from the UsageToBeReported collection
    let removeUsageToBeReported x s = { s with UsageToBeReported = (s.UsageToBeReported |> List.filter (fun e -> e <> x)) }

type UpdateOutOfOrderError =
    { DataWatermark: MessagePosition 
      UpdateWatermark: MessagePosition }

type BusinessDataUpdateError = 
    | UpdateOutOfOrderError of UpdateOutOfOrderError
    | SnapshotDownloadError of Exception

type UsageSubmittedToAPIResult = // Once the metering API was called, either the metering submission successfully got through, or not (in which case we need to know which values haven't been submitted)
    Result<MeteringAPIUsageEventDefinition, Exception * MeteringAPIUsageEventDefinition>

/// The events which are processed by the aggregator.
type MeteringUpdateEvent =
    /// Event to initialize the aggregator.
    | SubscriptionPurchased of SubscriptionCreationInformation 

    /// Event representing usage / consumption. Send from the application to the aggregator.
    | UsageReported of InternalUsageEvent
    
    /// An aggregator-internal event to keep track of which events must be / have been submitted to the metering API.
    | UsageSubmittedToAPI of UsageSubmittedToAPIResult

    /// A heart beat signal to flush potential billing periods
    | AggregatorBooted

type MeteringEvent =
    { MeteringUpdateEvent: MeteringUpdateEvent
      MessagePosition: MessagePosition }

type MeteringClient = 
    InternalUsageEvent -> System.Threading.Tasks.Task

type CurrentTimeProvider =
    unit -> MeteringDateTime

module CurrentTimeProvider =
    let LocalSystem : CurrentTimeProvider = (fun () -> ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc))
    let AlwaysReturnSameTime (time : MeteringDateTime) : CurrentTimeProvider = (fun () -> time)

type SubmitMeteringAPIUsageEvent =
    MeteringAPIUsageEventDefinition -> System.Threading.Tasks.Task

module SubmitMeteringAPIUsageEvent =
    let Discard : SubmitMeteringAPIUsageEvent = (fun _ -> System.Threading.Tasks.Task.CompletedTask)

type MeteringConfigurationProvider = 
    { CurrentTimeProvider: CurrentTimeProvider
      SubmitMeteringAPIUsageEvent: SubmitMeteringAPIUsageEvent 
      GracePeriod: Duration }

type BusinessLogic = // The business logic takes the current state, and a command to be applied, and returns new state
    MeteringState option -> MeteringEvent -> MeteringState option 

