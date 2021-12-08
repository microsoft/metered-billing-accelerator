namespace Metering

open System
open System.Threading
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Producer
open Metering.Types

[<Extension>]
module MeteringEventHubExtensions =    
    // [<Extension>] // Currently not exposed to C#
    let private SubmitMeteringUpdateEvent (eventHubProducerClient: EventHubProducerClient) (meteringUpdateEvent: MeteringUpdateEvent) (cancellationToken: CancellationToken) : Task =
        task {
            let! eventBatch = eventHubProducerClient.CreateBatchAsync(
                options = new CreateBatchOptions (PartitionKey = (meteringUpdateEvent |> MeteringUpdateEvent.partitionKey)),
                cancellationToken = cancellationToken)
            
            let eventData = 
                meteringUpdateEvent
                |> Json.toStr 0
                |> (fun x -> new BinaryData(x))
                |> (fun x -> new EventData(x))
            
            eventData.Properties.Add("SendingApplication", (System.Reflection.Assembly.GetEntryAssembly().FullName))
            if not (eventBatch.TryAdd(eventData))
            then failwith "The event could not be added."
            else ()

            return! eventHubProducerClient.SendAsync(
                eventBatch = eventBatch,
                // options: new SendEventOptions() {  PartitionId = "...", PartitionKey = "..." },
                cancellationToken = cancellationToken)
        }

    let private SubmitUsage (eventHubProducerClient: EventHubProducerClient) (ct: CancellationToken) (internalUsageEvent: InternalUsageEvent) =
        SubmitMeteringUpdateEvent eventHubProducerClient (UsageReported internalUsageEvent) ct

    let private SubmitQuantityManagedAppAsync (eventHubProducerClient: EventHubProducerClient) (meterName: string) (cancellationToken: CancellationToken) (quantity: Quantity) =    
        { InternalUsageEvent.InternalResourceId = InternalResourceId.ManagedApp
          Quantity = quantity
          Timestamp = MeteringDateTime.now(); MeterName = meterName |> ApplicationInternalMeterName.create; Properties = None
        }
        |> SubmitUsage eventHubProducerClient cancellationToken

    let private SubmitQuantitySaasAsync (eventHubProducerClient: EventHubProducerClient) (saasId: string) (meterName: string) (cancellationToken: CancellationToken) (quantity: Quantity) =
        { InternalUsageEvent.InternalResourceId = saasId |> SaaSSubscriptionID.create |> InternalResourceId.SaaSSubscription
          Quantity = quantity
          Timestamp = MeteringDateTime.now(); MeterName = meterName |> ApplicationInternalMeterName.create; Properties = None
        }
        |> SubmitUsage eventHubProducerClient cancellationToken
        
    [<Extension>]
    [<CompiledName("SubmitManagedAppMeterAsync")>]
    let SubmitManagedAppIntegerAsync (eventHubProducerClient: EventHubProducerClient) (meterName: string) (quantity: uint64) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        quantity |> Quantity.createInt |> SubmitQuantityManagedAppAsync eventHubProducerClient meterName cancellationToken
    
    [<Extension>]
    [<CompiledName("SubmitManagedAppMeterAsync")>]
    let SubmitManagedAppFloatAsync (eventHubProducerClient: EventHubProducerClient) (meterName: string) (quantity: decimal) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        quantity |> Quantity.createFloat |> SubmitQuantityManagedAppAsync eventHubProducerClient meterName cancellationToken

    [<Extension>]
    [<CompiledName("SubmitSaaSMeterAsync")>] // Naming these for C# method overloading
    let SubmitSaasIntegerAsync (eventHubProducerClient: EventHubProducerClient) (saasId: string) (meterName: string) (quantity: uint64) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        quantity |> Quantity.createInt |> SubmitQuantitySaasAsync eventHubProducerClient saasId meterName cancellationToken

    [<Extension>]
    [<CompiledName("SubmitSaaSMeterAsync")>] // Naming these for C# method overloading
    let SubmitSaasFloatAsync (eventHubProducerClient: EventHubProducerClient) (saasId: string) (meterName: string) (quantity: decimal) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        quantity |> Quantity.createFloat |> SubmitQuantitySaasAsync eventHubProducerClient saasId meterName cancellationToken

    [<Extension>]
    [<CompiledName("SubmitSubscriptionCreationAsync")>]
    let SubmitSubscriptionCreation (eventHubProducerClient: EventHubProducerClient) (sci: SubscriptionCreationInformation) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        SubmitMeteringUpdateEvent eventHubProducerClient (SubscriptionPurchased sci) cancellationToken

    let ReportUsageSubmitted (eventHubProducerClient: EventHubProducerClient) (msr: MarketplaceSubmissionResult) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        SubmitMeteringUpdateEvent eventHubProducerClient (UsageSubmittedToAPI msr) cancellationToken
