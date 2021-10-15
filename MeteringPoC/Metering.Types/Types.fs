namespace Metering.Types

open System
open Azure.Core
open Azure.Storage.Blobs
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Processor
open NodaTime

type BusinessError =
    | DayBeforeSubscription
    | NewDateFromPreviousBillingPeriod     

type SequenceNumber = SequenceNumber of uint64

type PartitionID = PartitionID of string

type MessagePosition = 
    { PartitionID: PartitionID
      SequenceNumber: SequenceNumber
      PartitionTimestamp: DateTime }

type SeekPosition =
    | FromSequenceNumber of SequenceNumber: SequenceNumber 
    | Earliest
    | FromTail

type EventHubConnectionDetails =
    { Credential: TokenCredential 
      EventHubNamespace: string
      EventHubName: string
      ConsumerGroupName: string
      CheckpointStorage: BlobContainerClient }

type Event =
    { EventData: EventData
      LastEnqueuedEventProperties: LastEnqueuedEventProperties
      PartitionContext: PartitionContext }

type EventHubProcessorEvent =
    | Event of Event
    | Error of ProcessErrorEventArgs
    | PartitionInitializing of PartitionInitializingEventArgs
    | PartitionClosing of PartitionClosingEventArgs

// https://docs.microsoft.com/en-us/azure/marketplace/marketplace-metering-service-apis#metered-billing-single-usage-event

type IntOrFloat =
    | Int of uint32 // ? :-)
    | Float of double
    
type PlanId = string

type DimensionId = string

type UnitOfMeasure = string

type Quantity = uint64
type IncludedQuantityMonthly = Quantity
type IncludedQuantityAnnually = Quantity

type IncludedMonthlyQuantity = { Quantity: Quantity }

type IncludedAnnualQuantity = { Quantity: Quantity }

type IncludedQuantity =
    { Monthly: IncludedMonthlyQuantity option
      Annual: IncludedAnnualQuantity option }

// https://docs.microsoft.com/en-us/azure/marketplace/azure-app-metered-billing#billing-dimensions
type BillingDimension =
    { DimensionId: DimensionId
      DimensionName: string 
      UnitOfMeasure: UnitOfMeasure
      IncludedQuantity: IncludedQuantity }
      
type Plan =
    { PlanId: PlanId
      BillingDimensions: BillingDimension seq }
      
type MeteredBillingSingleUsageEvent =
    { ResourceID: string // unique identifier of the resource against which usage is emitted. 
      Quantity: IntOrFloat // how many units were consumed for the date and hour specified in effectiveStartTime, must be greater than 0, can be integer or float value
      DimensionId: DimensionId // custom dimension identifier
      EffectiveStartTime: DateTime // time in UTC when the usage event occurred, from now and until 24 hours back
      PlanId: PlanId } // id of the plan purchased for the offer
    
type MeteredBillingBatchUsageEvent = 
    MeteredBillingSingleUsageEvent seq


type PlanRenewalInterval =
    | Monthly
    | Yearly

type Subscription = // When a certain plan was purchased
    { PlanId: PlanId
      PlanRenewalInterval: PlanRenewalInterval 
      SubscriptionStart: LocalDate }

type BillingPeriod =
    { FirstDay: LocalDate
      LastDay: LocalDate
      Index: uint }

type PlanDimension =
    { PlanId: PlanId
      DimensionId: DimensionId }

type ApplicationInternalMeterName = string // A meter name used between app and aggregator

type InternalMetersMapping = // The mapping table used by the aggregator to translate an ApplicationInternalMeterName to the plan and dimension configured in Azure marketplace
    Map<ApplicationInternalMeterName, PlanDimension>

type ConsumedQuantity = { Quantity: Quantity }

type MeterValue =
    | IncludedQuantity of IncludedQuantity
    | ConsumedQuantity of ConsumedQuantity

type CurrentMeterValues =
    Map<ApplicationInternalMeterName, MeterValue> 

type InternalUsageEvent = // From app to aggregator
    { Timestamp: DateTime
      MeterName: ApplicationInternalMeterName
      Quantity: Quantity
      Properties: Map<string, string> option}
      
type MeteringAPIUsageEventDefinition = // From aggregator to metering API
    { ResourceId: string 
      Quantity: double 
      PlanDimension: PlanDimension
      EffectiveStartTime: DateTime }
    
type CurrentBillingState =
    {
        Plans: Plan seq // The list of all plans. Certainly not needed in the aggregator?
        InitialPurchase: Subscription // The purchase information of the subscription
        InternalMetersMapping: InternalMetersMapping // The table mapping app-internal meter names to 'proper' ones for marketplace
        CurrentMeterValues: CurrentMeterValues // The current meter values in the aggregator
        UsageToBeReported: MeteringAPIUsageEventDefinition list // a list of usage elements which hasn't been reported yet to the metering API
        LastProcessedMessage: MessagePosition // Pending HTTP calls to the marketplace API
    } 

