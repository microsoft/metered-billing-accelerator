namespace Metering.Types

open System
open Azure.Core
open Azure.Storage.Blobs
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Processor

type SequenceNumber = SequenceNumber of int64

type PartitionID = PartitionID of string

type MessagePosition = 
    { PartitionID: PartitionID
      SequenceNumber: SequenceNumber }

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
    | Int of uint64
    | Float of float
    
type PlanID = string

type DimensionIdentifier = string

type UnitOfMeasure = string

type Quantity = uint64
type IncludedQuantityMonthly = Quantity
type IncludedQuantityAnnually = Quantity

// https://docs.microsoft.com/en-us/azure/marketplace/azure-app-metered-billing#billing-dimensions
type BillingDimension =
    { DimensionIdentifier: DimensionIdentifier
      DimensionName: string 
      UnitOfMeasure: UnitOfMeasure
      IncludedQuantityMonthly: IncludedQuantityMonthly option }

type MeteredBillingSingleUsageEvent =
    { ResourceID: string // unique identifier of the resource against which usage is emitted. 
      Quantity: IntOrFloat // how many units were consumed for the date and hour specified in effectiveStartTime, must be greater than 0, can be integer or float value
      Dimension: DimensionIdentifier // custom dimension identifier
      EffectiveStartTime: DateTime // time in UTC when the usage event occurred, from now and until 24 hours back
      PlanID: PlanID } // id of the plan purchased for the offer
    
type MeteredBillingBatchUsageEvent = 
    MeteredBillingSingleUsageEvent seq

type Plan =
    { Id: PlanID
      BillingDimensions: BillingDimension seq }

type Plans = 
    Plan seq

type UsageEvent =
    { PlanID: PlanID
      Timestamp: DateTime
      Dimension: DimensionIdentifier
      Quantity: Quantity
      Properties: Map<string, string> option}

type PlanPurchaseInformation =
    { PlanId: PlanID 
      PurchaseTimestamp: DateTime }

type RemainingQuantity = Quantity
type ConsumedQuantity = Quantity

type CurrentConsumptionBillingPeriod =
    | RemainingQuantity of RemainingQuantity
    | ConsumedQuantity of ConsumedQuantity

type CurrentCredits =
    Map<DimensionIdentifier, CurrentConsumptionBillingPeriod> 

type CurrentBillingState =
    { Plans: Plans
      InitialPurchase: PlanPurchaseInformation
      CurrentCredits: CurrentCredits }

module BusinessLogic =
    let deduct (reported: Quantity) (state: CurrentConsumptionBillingPeriod) : CurrentConsumptionBillingPeriod option =
        let inspect msg a =
            printf "%s: %A | " msg a
            a
        let inspectn msg a =
            printfn "%s: %A" msg a
            a

        reported
        |> inspect "reported"
        |> ignore

        state
        |> inspect "before"
        |> function
            | RemainingQuantity(remaining) -> 
                if remaining > reported 
                then RemainingQuantity(remaining - reported)
                else ConsumedQuantity(reported - remaining)
            | ConsumedQuantity(consumed) ->
                ConsumedQuantity(consumed + reported)
        |> inspectn "after"
        |> Some
    
    let applyConsumption (amount: Quantity) (current: CurrentConsumptionBillingPeriod option) : CurrentConsumptionBillingPeriod option =
        Option.bind (deduct amount) current

    let applyUsageEvent (current: CurrentBillingState) (event: UsageEvent) : CurrentBillingState =

        let newCredits = 
            current.CurrentCredits
            |> Map.change event.Dimension (applyConsumption event.Quantity)
            
        { current 
            with CurrentCredits = newCredits}

    let applyUsageEvents (state: CurrentBillingState) (usageEvents: UsageEvent list) :CurrentBillingState =
        usageEvents |> List.fold applyUsageEvent state

