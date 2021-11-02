namespace Metering.Types

open System
open NodaTime
open Metering.Types.EventHub

type Quantity =
    | MeteringInt of uint64
    | MeteringFloat of Decimal

    /// Indicates that this plan participates in this dimension, but does not emit usage against this dimension. 
    | Infinite // https://docs.microsoft.com/en-us/azure/marketplace/partner-center-portal/saas-metered-billing

    static member (+) (a: Quantity, b: Quantity) =
        match (a, b) with
            | ((MeteringInt a), (MeteringInt b)) -> MeteringInt  (a + b)
            | ((MeteringInt a), (MeteringFloat b)) -> MeteringFloat (Decimal a + b)
            | ((MeteringFloat a), (MeteringInt b)) -> MeteringFloat (a + Decimal b)
            | ((MeteringFloat a), (MeteringFloat b)) -> MeteringFloat (a + b)
            | (_, _) -> Infinite

    static member (-) (a: Quantity, b: Quantity) =
        match (a, b) with
            | ((MeteringInt a), (MeteringInt b)) -> MeteringInt (a - b)
            | ((MeteringInt a), (MeteringFloat b)) -> MeteringFloat (Decimal a - b)
            | ((MeteringFloat a), (MeteringInt b)) -> MeteringFloat (a - Decimal b)
            | ((MeteringFloat a), (MeteringFloat b)) -> MeteringFloat (a - b)
            | (Infinite, MeteringInt _) -> Infinite
            | (Infinite, MeteringFloat _) -> Infinite
            | (_, Infinite) -> failwith "This must never happen"

    //static member createInt i = (MeteringInt i)
    //static member someInt = (Quantity.createInt |> Some)
    //
    //member this.valueAsInt =
    //    match this with 
    //    | MeteringInt i -> i
    //    | MeteringFloat f -> uint64 f
        
module Quantity =
    let createInt i = (MeteringInt i)
    let createFloat f = (MeteringFloat f)

    let someInt = createInt >> Some
    let someFloat = createFloat >> Some
    let none : (Quantity option) = None

    let valueAsInt = function
        | MeteringInt i -> i
        | MeteringFloat f -> uint64 f
        | Infinite -> failwith "Infinity"

    let valueAsFloat = function
        | MeteringInt i -> decimal i
        | MeteringFloat f -> f
        | Infinite -> failwith "Infinity"

type ConsumedQuantity = 
    { Amount: Quantity
      Created: MeteringDateTime 
      LastUpdate: MeteringDateTime }

type IncludedQuantitySpecification = 
    { /// Monthly quantity included in base.
      /// Quantity of dimension included per month for customers paying the recurring monthly fee, must be an integer. It can be 0 or unlimited.
      Monthly: Quantity option

      /// Annual quantity included in base.
      /// Quantity of dimension included per each year for customers paying the recurring annual fee, must be an integer. Can be 0 or unlimited.
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

    let createMonthly now amount =  IncludedQuantity { Annually = None; Monthly = Some amount; Created = now; LastUpdate = now }
    let createAnnually now amount =  IncludedQuantity { Annually = Some amount; Monthly = None; Created = now; LastUpdate = now }
    let create now monthlyAmount annualAmount =  IncludedQuantity { Annually = Some annualAmount; Monthly = Some monthlyAmount; Created = now; LastUpdate = now }
    let setAnnually now amount q = { q with Annually = Some amount ; LastUpdate = now }
    let setMonthly now amount q = { q with Monthly = Some amount ; LastUpdate = now }
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
    type PlanId = private PlanId of string

    module PlanId = 
        let value (PlanId x) = x
        let create x = (PlanId x)

    /// The immutable dimension identifier referenced while emitting usage events.
    type DimensionId = private DimensionId of string

    module DimensionId = 
        let value (DimensionId x) = x
        let create x = (DimensionId x)

    /// The description of the billing unit, for example "per text message" or "per 100 emails".
    type UnitOfMeasure = private UnitOfMeasure of string

    module UnitOfMeasure = 
        let value (UnitOfMeasure x) = x
        let create x = (UnitOfMeasure x)

    // https://docs.microsoft.com/en-us/azure/marketplace/azure-app-metered-billing#billing-dimensions
    /// Defines a custom unit by which the ISV can emit usage events. 
    /// Billing dimensions are also used to communicate to the customer about how they will be billed for using the software. 
    type BillingDimension =
        { /// The immutable dimension identifier referenced while emitting usage events.
          DimensionId: DimensionId
          
          /// The display name associated with the dimension, for example "text messages sent".
          DimensionName: string 
          
          /// The description of the billing unit, for example "per text message" or "per 100 emails".
          UnitOfMeasure: UnitOfMeasure
          IncludedQuantity: IncludedQuantitySpecification }
      
    type Plan =
        { PlanId: PlanId
          BillingDimensions: BillingDimension seq }

    /// For Azure Application Managed Apps plans, the resourceId is the Managed App resource group Id. +
    type ManagedAppResourceGroupID = private ManagedAppResourceGroupID of string
    
    module ManagedAppResourceGroupID =
        let value (ManagedAppResourceGroupID x) = x

        let create x = (ManagedAppResourceGroupID x)

    /// For SaaS offers, the resourceId is the SaaS subscription ID. 
    type SaaSSubscriptionID = private SaaSSubscriptionID of string

    module SaaSSubscriptionID =
        let value (SaaSSubscriptionID x) = x

        let create x = (SaaSSubscriptionID x)
        
    /// Unique identifier of the resource against which usage is emitted. 
    type ResourceID = // https://docs.microsoft.com/en-us/azure/marketplace/marketplace-metering-service-apis#metered-billing-single-usage-event
        | ManagedAppResourceGroupID of ManagedAppResourceGroupID
        | SaaSSubscriptionID of SaaSSubscriptionID

    module ResourceID =
        let createFromManagedAppResourceGroupID x = x |> ManagedAppResourceGroupID.create |> ManagedAppResourceGroupID

        let createFromSaaSSubscriptionID x = x |> SaaSSubscriptionID.create |> SaaSSubscriptionID

    /// This is the key by which to aggregate across multiple tenants
    type SubscriptionType =
        | ManagedApp
        | SaaSSubscription of SaaSSubscriptionID

    module SubscriptionType =
        let private ManagedAppMarkerString = "AzureManagedApplication"

        let fromStr s =
            if String.Equals(s, ManagedAppMarkerString)
            then ManagedApp
            else s |> SaaSSubscriptionID.create |> SaaSSubscription

        let toStr = 
            function
            | ManagedApp -> ManagedAppMarkerString
            | SaaSSubscription x -> x |> SaaSSubscriptionID.value

    // https://docs.microsoft.com/en-us/azure/marketplace/marketplace-metering-service-apis#metered-billing-single-usage-event
    type MeteredBillingUsageEvent =
        { 
          /// Unique identifier of the resource against which usage is emitted. 
          ResourceID: ResourceID 
          
          /// How many units were consumed for the date and hour specified in effectiveStartTime, must be greater than 0, can be integer or float value
          Quantity: Quantity 
          
          /// Custom dimension identifier.
          DimensionId: DimensionId
          
          /// Time in UTC when the usage event occurred, from now and until 24 hours back.
          EffectiveStartTime: MeteringDateTime 
          
          /// ID of the plan purchased for the offer.
          PlanId: PlanId } 

    type MeteredBillingUsageEventBatch = 
        MeteredBillingUsageEvent list
 
open MarketPlaceAPI

type ApplicationInternalMeterName = private ApplicationInternalMeterName of string // A meter name used between app and aggregator

module ApplicationInternalMeterName =
    let value (ApplicationInternalMeterName x) = x
    let create x = (ApplicationInternalMeterName x)

type InternalUsageEvent = // From app to aggregator
    { Scope: SubscriptionType
      Timestamp: MeteringDateTime  // timestamp from the sending app
      MeterName: ApplicationInternalMeterName
      Quantity: Quantity
      Properties: Map<string, string> option}

type BusinessError =
    | DayBeforeSubscription

type Subscription = 
    { Plan: Plan
      SubscriptionType: SubscriptionType
      RenewalInterval: RenewalInterval 
      SubscriptionStart: MeteringDateTime } // When a certain plan was purchased

module Subscription =
    let create plan subType pri subscriptionStart =
        { Plan = plan
          SubscriptionType = subType
          RenewalInterval = pri
          SubscriptionStart = subscriptionStart }

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
    { ResourceId: ResourceID 
      SubscriptionType: SubscriptionType
      Quantity: decimal 
      PlanDimension: PlanDimension
      EffectiveStartTime: MeteringDateTime }

/// Event representing the creation of a subscription. 
type SubscriptionCreationInformation =
    { Subscription: Subscription // The purchase information of the subscription
      InternalMetersMapping: InternalMetersMapping } // The table mapping app-internal meter names to 'proper' ones for marketplace
    
type Meter =
    { Subscription: Subscription // The purchase information of the subscription
      InternalMetersMapping: InternalMetersMapping // The table mapping app-internal meter names to 'proper' ones for marketplace
      CurrentMeterValues: CurrentMeterValues // The current meter values in the aggregator
      UsageToBeReported: MeteringAPIUsageEventDefinition list // a list of usage elements which haven't yet been reported to the metering API
      LastProcessedMessage: MessagePosition } // Last message which has been applied to this Meter
        
module Meter =
    let setCurrentMeterValues x s = { s with CurrentMeterValues = x }
    let applyToCurrentMeterValue f s = { s with CurrentMeterValues = (f s.CurrentMeterValues) }
    let setLastProcessedMessage x s = { s with LastProcessedMessage = x }
    let addUsageToBeReported x s = { s with UsageToBeReported = (x :: s.UsageToBeReported) }
    let addUsagesToBeReported x s = { s with UsageToBeReported = List.concat [ x; s.UsageToBeReported ] }
    
    /// Removes the item from the UsageToBeReported collection
    let removeUsageToBeReported x s = { s with UsageToBeReported = (s.UsageToBeReported |> List.filter (fun e -> e <> x)) }

type MeterCollection = Map<SubscriptionType, Meter>

module MeterCollection =
    let empty : MeterCollection = Map.empty

    let lastUpdate (meters: MeterCollection) : MessagePosition option = 
        if meters |> Seq.isEmpty 
        then None
        else
            meters
            |> Map.toSeq
            |> Seq.maxBy (fun (_subType, meter) -> meter.LastProcessedMessage.SequenceNumber)
            |> (fun (_, meter) -> meter.LastProcessedMessage)
            |> Some

type UpdateOutOfOrderError =
    { DataWatermark: MessagePosition 
      UpdateWatermark: MessagePosition }

type BusinessDataUpdateError = 
    | UpdateOutOfOrderError of UpdateOutOfOrderError
    | SnapshotDownloadError of Exception

type UsageSubmittedToAPIResult = // Once the metering API was called, either the metering submission successfully got through, or not (in which case we need to know which values haven't been submitted)
    { Payload: MeteringAPIUsageEventDefinition 
      Result: Result<unit, Exception> }
    
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
    MeteringAPIUsageEventDefinition -> Async<unit>

module SubmitMeteringAPIUsageEvent =
    let Discard : SubmitMeteringAPIUsageEvent = (fun _ -> Async.AwaitTask System.Threading.Tasks.Task.CompletedTask)

type MeteringConfigurationProvider = 
    { CurrentTimeProvider: CurrentTimeProvider
      SubmitMeteringAPIUsageEvent: SubmitMeteringAPIUsageEvent 
      GracePeriod: Duration }

type BusinessLogic = // The business logic takes the current state, and a command to be applied, and returns new state
    Meter option -> MeteringEvent -> Meter option 

