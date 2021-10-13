namespace Metering.Types

open System
open Azure.Core
open Azure.Storage.Blobs
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Processor

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

type Message<'payload> =
    { Payload: 'payload 
      MessagePosition: MessagePosition }

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

// https://docs.microsoft.com/en-us/azure/marketplace/azure-app-metered-billing#billing-dimensions
type BillingDimension =
    { DimensionId: DimensionId
      DimensionName: string 
      UnitOfMeasure: UnitOfMeasure
      IncludedQuantityMonthly: IncludedQuantityMonthly option }

type MeteredBillingSingleUsageEvent =
    { ResourceID: string // unique identifier of the resource against which usage is emitted. 
      Quantity: IntOrFloat // how many units were consumed for the date and hour specified in effectiveStartTime, must be greater than 0, can be integer or float value
      DimensionId: DimensionId // custom dimension identifier
      EffectiveStartTime: DateTime // time in UTC when the usage event occurred, from now and until 24 hours back
      PlanId: PlanId } // id of the plan purchased for the offer
    
type MeteredBillingBatchUsageEvent = 
    MeteredBillingSingleUsageEvent seq

type Plan =
    { PlanId: PlanId
      BillingDimensions: BillingDimension seq }

type UsageEvent =
    { Timestamp: DateTime
      PlanId: PlanId
      DimensionId: DimensionId      
      Quantity: Quantity
      Properties: Map<string, string> option}

type PlanPurchaseInformation =
    { PlanId: PlanId 
      PurchaseTimestamp: DateTime }

type RemainingQuantity = 
    { Quantity: Quantity }

type ConsumedQuantity =
    { Quantity: Quantity }

type LastUpdateTimestamp = DateTime

type CurrentConsumptionBillingPeriod =
    | RemainingQuantity of RemainingQuantity
    | ConsumedQuantity of ConsumedQuantity

//type BillingPeriod =
    

type PlanDimension =
    { PlanId: PlanId
      DimensionId: DimensionId }

type CurrentCredits =
    Map<PlanDimension, CurrentConsumptionBillingPeriod> 

type UsageEventDefinition =
    { ResourceId: string 
      Quantity: double 
      PlanDimension: PlanDimension
      EffectiveStartTime: DateTime }
    
type CurrentBillingState =
    { Plans: Plan seq
      InitialPurchase: PlanPurchaseInformation
      CurrentCredits: CurrentCredits
      UsageToBeReported: UsageEventDefinition list
      LastProcessedMessage: MessagePosition } // Pending HTTP calls to the marketplace API

module BusinessLogic =
    let deduct ({ Quantity = reported}: UsageEvent) (state: CurrentConsumptionBillingPeriod) : CurrentConsumptionBillingPeriod option =

        state
        |> function
            | RemainingQuantity({ Quantity = remaining}) -> 
                if remaining > reported
                then RemainingQuantity({ Quantity = remaining - reported})
                else ConsumedQuantity({ Quantity = reported - remaining})
            | ConsumedQuantity(consumed) ->
                ConsumedQuantity({ Quantity = consumed.Quantity + reported })
        |> Some
    
    let applyConsumption (event: UsageEvent) (current: CurrentConsumptionBillingPeriod option) : CurrentConsumptionBillingPeriod option =
        Option.bind (deduct event) current

    let applyUsageEvent (current: CurrentBillingState) (event: UsageEvent) : CurrentBillingState =

        let newCredits = 
            current.CurrentCredits
            |> Map.change { PlanId = event.PlanId; DimensionId = event.DimensionId } (applyConsumption event)
            
        { current 
            with CurrentCredits = newCredits}

    let applyUsageEvents (state: CurrentBillingState) (usageEvents: UsageEvent list) : CurrentBillingState =
        usageEvents |> List.fold applyUsageEvent state

