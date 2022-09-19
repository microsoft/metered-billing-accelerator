// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.ClientSDK

open System
open System.Threading
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Microsoft.Extensions.DependencyInjection
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Producer
open Metering.BaseTypes
open Metering.BaseTypes.EventHub
open Metering.Integration

type MeterValues = 
    { Quantity: Quantity
      Name: ApplicationInternalMeterName }

    [<CompiledName("create")>]
    static member createInt (applicationInternalName: string) (quantity: uint) =
        { MeterValues.Quantity = quantity |> Quantity.create
          Name = applicationInternalName |> ApplicationInternalMeterName.create }

    [<CompiledName("create")>]
    static member createFloat (applicationInternalName: string) (quantity: float) =
        { MeterValues.Quantity = quantity |> Quantity.create
          Name = applicationInternalName |> ApplicationInternalMeterName.create }        
        
type Consumption =
    { InternalResourceId: InternalResourceId 
      Meters: MeterValues list }

    static member create (identifier: string, [<ParamArray>] vals: MeterValues array) =
        { InternalResourceId = identifier |> InternalResourceId.fromStr
          Meters = vals |> Array.toList }

[<Extension>]
module MeteringEventHubExtensions =    
    let sendingApp =  System.Reflection.Assembly.GetEntryAssembly().FullName

    let private createEventData (resourceId: string) (meteringUpdateEvent: MeteringUpdateEvent) : EventData =
        meteringUpdateEvent
        |> Json.toStr 0
        |> (fun x -> new BinaryData(x))
        |> (fun x -> 
            let eventData = new EventData(eventBody = x, ContentType = "application/json")
            eventData.Properties.Add("resourceId", resourceId)
            eventData.Properties.Add("SendingApplication", sendingApp)
            eventData
        )

    let private addEvent (batch: EventDataBatch) (eventData: EventData) =
        if not (batch.TryAdd(eventData))
        then failwith "The event could not be added."
        else ()

    // [<Extension>] // Currently not exposed to C#
    let private SubmitMeteringUpdateEvent (eventHubProducerClient: EventHubProducerClient) (cancellationToken: CancellationToken) (meteringUpdateEvents: MeteringUpdateEvent list) : Task =
        task {
            // the public functions and type design in this module ensure all events from a call go to the same partition
            let partitionKey = 
                meteringUpdateEvents
                |> List.head
                |> (fun f -> f.partitionKey)
            
            let! eventBatch = eventHubProducerClient.CreateBatchAsync(
                options = new CreateBatchOptions (PartitionKey = partitionKey),
                cancellationToken = cancellationToken)
            
            meteringUpdateEvents
            |> List.map (createEventData partitionKey)
            |> List.iter (addEvent eventBatch)

            return! eventHubProducerClient.SendAsync(eventBatch = eventBatch, cancellationToken = cancellationToken)
        }

    let private SubmitMeteringUpdateEventToPartition (eventHubProducerClient: EventHubProducerClient) (partitionId: PartitionID) (cancellationToken: CancellationToken) (meteringUpdateEvent: MeteringUpdateEvent) : Task =
        task {
            let! eventBatch = eventHubProducerClient.CreateBatchAsync(
                options = new CreateBatchOptions (PartitionId = partitionId.value),
                cancellationToken = cancellationToken)
            
            meteringUpdateEvent
            |> createEventData (meteringUpdateEvent.partitionKey)
            |> addEvent eventBatch

            return! eventHubProducerClient.SendAsync(eventBatch = eventBatch, cancellationToken = cancellationToken)
        }

    let enforceNonNegativeAndNonInfiniteQuantity (q: Quantity) =
        match q with        
        | MeteringInt _ -> () // no need to check an integer which can't be negative
        | MeteringFloat f -> 
            if f < 0.0
            then raise (new ArgumentException(message = $"Not allowed to submit negative metering values like {f}"))
        | Infinite -> raise (new ArgumentException(message = "Not allowed to submit infinite consumption"))

    [<Extension>]
    let SubmitManagedAppMetersAsync (eventHubProducerClient: EventHubProducerClient) (resourceId: string) (meters: MeterValues seq) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        meters
        |> Seq.map (fun v -> enforceNonNegativeAndNonInfiniteQuantity v.Quantity; v)
        |> Seq.map(fun v -> 
            { InternalResourceId = InternalResourceId.fromResourceID resourceId
              Quantity = v.Quantity
              Timestamp = MeteringDateTime.now()
              MeterName = v.Name
              Properties = None } 
            |> UsageReported
        )
        |> Seq.toList
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken

    [<Extension>]
    let SubmitManagedAppMeterAsync (eventHubProducerClient: EventHubProducerClient) (resourceId: string) (meter: MeterValues) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =        
        SubmitManagedAppMetersAsync eventHubProducerClient resourceId [| meter |] cancellationToken

    [<Extension>]
    [<CompiledName("SubmitSaaSMetersAsync")>] // Naming these for C# method overloading
    let SubmitSaaSMetersAsync (eventHubProducerClient: EventHubProducerClient) (saasSubscriptionId: string)  (meters: MeterValues seq) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        meters
        |> Seq.map (fun v -> enforceNonNegativeAndNonInfiniteQuantity v.Quantity; v)
        |> Seq.map (fun v -> 
            { InternalUsageEvent.InternalResourceId = saasSubscriptionId |> InternalResourceId.fromResourceID
              Quantity = v.Quantity
              Timestamp = MeteringDateTime.now()
              MeterName = v.Name
              Properties = None }
             |> UsageReported
        )
        |> Seq.toList
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken
        
    [<Extension>]
    [<CompiledName("SubmitSaaSMeterAsync")>] // Naming these for C# method overloading
    let submitSaaSMeterUIntAsync (eventHubProducerClient: EventHubProducerClient) (saasSubscriptionId: string) (applicationInternalMeterName: string) (quantity: uint) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        [
            { InternalUsageEvent.InternalResourceId = saasSubscriptionId |> InternalResourceId.fromResourceID
              Quantity = quantity |> Quantity.create
              Timestamp = MeteringDateTime.now()
              MeterName = applicationInternalMeterName |> ApplicationInternalMeterName.create
              Properties = None }
             |> UsageReported
        ]
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken

    [<Extension>]
    [<CompiledName("SubmitSaaSMeterAsync")>] // Naming these for C# method overloading
    let submitSaaSMeterFloatAsync (eventHubProducerClient: EventHubProducerClient) (saasSubscriptionId: string) (applicationInternalMeterName: string) (quantity: float) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        [
            { InternalUsageEvent.InternalResourceId = saasSubscriptionId |> InternalResourceId.fromResourceID
              Quantity = quantity |> Quantity.create
              Timestamp = MeteringDateTime.now()
              MeterName = applicationInternalMeterName |> ApplicationInternalMeterName.create
              Properties = None }
             |> UsageReported
        ]
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken

    let private asListWithSingleElement<'T> (t: 'T) = [ t ] 

    [<Extension>]
    [<CompiledName("SubmitSubscriptionCreationAsync")>]
    let SubmitSubscriptionCreation (eventHubProducerClient: EventHubProducerClient) (sci: SubscriptionCreationInformation) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        SubscriptionPurchased sci
        |> asListWithSingleElement
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken

    [<Extension>]
    [<CompiledName("SubmitSubscriptionDeletionAsync")>]
    let SubmitSubscriptionDeletion (eventHubProducerClient: EventHubProducerClient) (resourceId: InternalResourceId) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        SubscriptionDeletion resourceId
        |> asListWithSingleElement
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken

    // this is not exposed as C# extension, as only F# is supposed to call it. 
    [<Extension>]
    let ReportUsagesSubmitted (eventHubProducerClient: EventHubProducerClient) ({ Results = items}: MarketplaceBatchResponse) (cancellationToken: CancellationToken) =
        items
        |> List.map UsageSubmittedToAPI 
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken

    [<Extension>]
    let RemoveUnprocessableMessagesUpTo(eventHubProducerClient: EventHubProducerClient) (partitionId: PartitionID) (sequenceNumber: SequenceNumber) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        { PartitionID = partitionId; Selection = sequenceNumber |> BeforeIncluding }
        |> RemoveUnprocessedMessages 
        |> SubmitMeteringUpdateEventToPartition eventHubProducerClient partitionId cancellationToken

    [<Extension>]
    let RemoveUnprocessableMessage(eventHubProducerClient: EventHubProducerClient) (partitionId: PartitionID) (sequenceNumber: SequenceNumber) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        { PartitionID = partitionId; Selection = sequenceNumber |> Exactly }
        |> RemoveUnprocessedMessages 
        |> SubmitMeteringUpdateEventToPartition eventHubProducerClient partitionId cancellationToken

    [<Extension>]
    let AddMeteringClientSDK (services: IServiceCollection) = 
        services.AddSingleton(MeteringConnections.createEventHubProducerClientForClientSDK())
