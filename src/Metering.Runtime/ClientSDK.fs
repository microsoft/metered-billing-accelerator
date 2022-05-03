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

type MeterValue = 
    { Quantity: Quantity
      Name: ApplicationInternalMeterName }

module MeterValue =
    [<CompiledName("create")>]
    let createInt (name: string) (quantity: uint) : MeterValue =
        { Quantity = quantity |> Quantity.create
          Name = name |> ApplicationInternalMeterName.create }

    [<CompiledName("create")>]
    let createFloat (name: string) (quantity: float) : MeterValue =
        { Quantity = quantity |> Quantity.create
          Name = name |> ApplicationInternalMeterName.create }        

type ManagedAppConsumption = 
    Meters of MeterValue list

module ManagedAppConsumption =
    let create ([<ParamArray>] vals: MeterValue array) =
        vals |> Array.toList |> Meters
        
type SaaSConsumption =
    { SaaSSubscriptionID: SaaSSubscriptionID 
      Meters: MeterValue list }

module SaaSConsumption =
    let create (saasId: string) ([<ParamArray>] vals: MeterValue array) =
        { SaaSSubscriptionID = saasId |> SaaSSubscriptionID.create
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
                options = new CreateBatchOptions (PartitionId = (partitionId |> PartitionID.value)),
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
    let SubmitManagedAppMetersAsync (eventHubProducerClient: EventHubProducerClient) (meters: MeterValue seq) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        meters
        |> Seq.map (fun v -> enforceNonNegativeAndNonInfiniteQuantity v.Quantity; v)
        |> Seq.map(fun v -> 
            { InternalResourceId = ManagedApplication ManagedAppIdentity
              Quantity = v.Quantity
              Timestamp = MeteringDateTime.now()
              MeterName = v.Name
              Properties = None } 
            |> UsageReported
        )
        |> Seq.toList
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken

    [<Extension>]
    let SubmitManagedAppMeterAsync (eventHubProducerClient: EventHubProducerClient) (meter: MeterValue) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =        
        SubmitManagedAppMetersAsync eventHubProducerClient [| meter |] cancellationToken

    [<Extension>]
    [<CompiledName("SubmitSaaSMeterAsync")>] // Naming these for C# method overloading
    let SubmitSaaSMeterAsync (eventHubProducerClient: EventHubProducerClient) (consumption: SaaSConsumption) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        consumption.Meters
        |> List.map (fun v -> enforceNonNegativeAndNonInfiniteQuantity v.Quantity; v)
        |> List.map (fun v -> 
            { InternalUsageEvent.InternalResourceId = consumption.SaaSSubscriptionID |> InternalResourceId.SaaSSubscription
              Quantity = v.Quantity
              Timestamp = MeteringDateTime.now()
              MeterName = v.Name
              Properties = None }
             |> UsageReported
        )
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
