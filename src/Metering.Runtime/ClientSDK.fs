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

    [<CompiledName("create")>]
    static member createInt (applicationInternalName: string) (quantity: uint) =
        { MeterValue.Quantity = quantity |> Quantity.create
          Name = applicationInternalName |> ApplicationInternalMeterName.create }

    [<CompiledName("create")>]
    static member createFloat (applicationInternalName: string) (quantity: float) =
        { MeterValue.Quantity = quantity |> Quantity.create
          Name = applicationInternalName |> ApplicationInternalMeterName.create }

type Consumption =
    { MarketplaceResourceId: MarketplaceResourceId
      Meters: MeterValue list }

    static member create (identifier: string, [<ParamArray>] vals: MeterValue array) =
        { MarketplaceResourceId = identifier |> MarketplaceResourceId.fromStr
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

    let private SubmitMeteringUpdateEventToPartitionID (eventHubProducerClient: EventHubProducerClient) (partitionId: PartitionID) (cancellationToken: CancellationToken) (meteringUpdateEvent: MeteringUpdateEvent) : Task =
        task {
            let! eventBatch = eventHubProducerClient.CreateBatchAsync(
                options = new CreateBatchOptions (PartitionId = partitionId.value),
                cancellationToken = cancellationToken)

            meteringUpdateEvent
            |> createEventData (meteringUpdateEvent.partitionKey)
            |> addEvent eventBatch

            return! eventHubProducerClient.SendAsync(eventBatch = eventBatch, cancellationToken = cancellationToken)
        }

    let private SubmitMeteringUpdateEventsToPartitionID (eventHubProducerClient: EventHubProducerClient) (partitionId: PartitionID) (cancellationToken: CancellationToken) (meteringUpdateEvents: MeteringUpdateEvent list) : Task =
        task {
            let! eventBatch = eventHubProducerClient.CreateBatchAsync(
                options = new CreateBatchOptions (PartitionId = partitionId.value),
                cancellationToken = cancellationToken)

            meteringUpdateEvents
            |> List.map (
                Json.toStr 0
                >> (fun x -> new BinaryData(x))
                >> (fun x -> new EventData(eventBody = x, ContentType = "application/json")))
            |> List.iter (addEvent eventBatch)

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
    [<CompiledName("SubmitMetersAsync")>] // Naming these for C# method overloading
    let SubmitMetersAsync (eventHubProducerClient: EventHubProducerClient) (resourceId: string) (meters: MeterValue seq) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        meters
        |> Seq.map (fun v -> enforceNonNegativeAndNonInfiniteQuantity v.Quantity; v)
        |> Seq.map (fun v ->
            { InternalUsageEvent.MarketplaceResourceId = resourceId |> MarketplaceResourceId.fromResourceID
              Quantity = v.Quantity
              Timestamp = MeteringDateTime.now()
              MeterName = v.Name
              Properties = None }
             |> UsageReported
        )
        |> Seq.toList
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken

    [<Extension>]
    [<CompiledName("SubmitMeterAsync")>] // Naming these for C# method overloading
    let submitMeterUIntAsync (eventHubProducerClient: EventHubProducerClient) (resourceId: string) (applicationInternalMeterName: string) (quantity: uint) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        [
            { InternalUsageEvent.MarketplaceResourceId = resourceId |> MarketplaceResourceId.fromResourceID
              Quantity = quantity |> Quantity.create
              Timestamp = MeteringDateTime.now()
              MeterName = applicationInternalMeterName |> ApplicationInternalMeterName.create
              Properties = None }
             |> UsageReported
        ]
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken

    [<Extension>]
    [<CompiledName("SubmitMeterAsync")>] // Naming these for C# method overloading
    let submitMeterFloatAsync (eventHubProducerClient: EventHubProducerClient) (resourceId: string) (applicationInternalMeterName: string) (quantity: float) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        [
            { InternalUsageEvent.MarketplaceResourceId = resourceId |> MarketplaceResourceId.fromResourceID
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
    let SubmitSubscriptionDeletion (eventHubProducerClient: EventHubProducerClient) (marketplaceResourceId: MarketplaceResourceId) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        SubscriptionDeletion marketplaceResourceId
        |> asListWithSingleElement
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken

    // this is not exposed as C# extension, as only F# is supposed to call it.
    [<Extension>]
    let ReportUsagesSubmitted (eventHubProducerClient: EventHubProducerClient) (partitionId: PartitionID) ({ Results = items}: MarketplaceBatchResponse) (cancellationToken: CancellationToken) =
        items
        |> List.map UsageSubmittedToAPI
        |> SubmitMeteringUpdateEventsToPartitionID eventHubProducerClient partitionId cancellationToken

    [<Extension>]
    let RemoveUnprocessableMessagesUpTo (eventHubProducerClient: EventHubProducerClient) (partitionId: PartitionID) (sequenceNumber: SequenceNumber) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        { PartitionID = partitionId; Selection = sequenceNumber |> BeforeIncluding }
        |> RemoveUnprocessedMessages
        |> SubmitMeteringUpdateEventToPartitionID eventHubProducerClient partitionId cancellationToken

    [<Extension>]
    let RemoveUnprocessableMessage (eventHubProducerClient: EventHubProducerClient) (partitionId: PartitionID) (sequenceNumber: SequenceNumber) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        { PartitionID = partitionId; Selection = sequenceNumber |> Exactly }
        |> RemoveUnprocessedMessages
        |> SubmitMeteringUpdateEventToPartitionID eventHubProducerClient partitionId cancellationToken

    [<Extension>]
    let SendPing (eventHubProducerClient: EventHubProducerClient) (message: PingMessage) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        Ping message
        |> SubmitMeteringUpdateEventToPartitionID eventHubProducerClient message.PartitionID cancellationToken

    [<Extension>]
    let AddMeteringClientSDK (services: IServiceCollection) =
        services.AddSingleton(MeteringConnections.createEventHubProducerClientForClientSDK())

