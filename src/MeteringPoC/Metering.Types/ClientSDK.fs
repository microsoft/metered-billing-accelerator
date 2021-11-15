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
    let SubmitMeteringUpdateEvent (eventHubProducerClient: EventHubProducerClient) (meteringUpdateEvent: MeteringUpdateEvent) ([<Optional; DefaultParameterValue(CancellationToken())>] ct: CancellationToken) : Task<unit> = task {        
        let createBatchOptions = 
            let cbo = new CreateBatchOptions()
            cbo.PartitionKey <- meteringUpdateEvent |> MeteringUpdateEvent.partitionKey
            cbo

        let! eventBatch = eventHubProducerClient.CreateBatchAsync(options = createBatchOptions, cancellationToken = ct)
        
        let eventData = 
            meteringUpdateEvent
            |> Json.toStr 0
            |> (fun x -> new BinaryData(x))
            |> (fun x -> new EventData(x))
        
        eventData.Properties.Add("SendingApplication", (System.Reflection.Assembly.GetEntryAssembly().FullName))
        if not (eventBatch.TryAdd(eventData))
        then failwith "The event could not be added."
        else ()

        // options: new SendEventOptions() {  PartitionId = "...", PartitionKey = "..." },
        let! () = eventHubProducerClient.SendAsync(eventBatch = eventBatch, cancellationToken = ct)

        return ()
    }

    let private SubmitUsage (eventHubProducerClient: EventHubProducerClient) (ct: CancellationToken) (internalUsageEvent: InternalUsageEvent) =
        SubmitMeteringUpdateEvent eventHubProducerClient (UsageReported internalUsageEvent) ct
    
    [<Extension>]
    let SubmitManagedAppIntegerAsync (eventHubProducerClient: EventHubProducerClient) (meterName: string) (quantity: uint64) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) : Task<unit> =
        { InternalUsageEvent.InternalResourceId = InternalResourceId.ManagedApp
          Quantity = quantity |> Quantity.createInt 
          Timestamp = MeteringDateTime.now(); MeterName = meterName |> ApplicationInternalMeterName.create; Properties = None
        }
        |> SubmitUsage eventHubProducerClient cancellationToken
    
    [<Extension>]
    let SubmitManagedAppFloatAsync (eventHubProducerClient: EventHubProducerClient) (meterName: string) (quantity: decimal) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) : Task<unit> =
        { InternalUsageEvent.InternalResourceId = InternalResourceId.ManagedApp
          Quantity = quantity |> Quantity.createFloat
          Timestamp = MeteringDateTime.now(); MeterName = meterName |> ApplicationInternalMeterName.create; Properties = None
        }
        |> SubmitUsage eventHubProducerClient cancellationToken

    [<Extension>]
    let SubmitSaasIntegerAsync (eventHubProducerClient: EventHubProducerClient) (saasId: string) (meterName: string) (quantity: uint64) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) : Task<unit> =
        { InternalUsageEvent.InternalResourceId = saasId |> SaaSSubscriptionID.create |> InternalResourceId.SaaSSubscription
          Quantity = quantity |> Quantity.createInt 
          Timestamp = MeteringDateTime.now(); MeterName = meterName |> ApplicationInternalMeterName.create; Properties = None
        }
        |> SubmitUsage eventHubProducerClient cancellationToken

    [<Extension>]
    let SubmitSaasFloatAsync (eventHubProducerClient: EventHubProducerClient) (saasId: string) (meterName: string) (quantity: decimal) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) : Task<unit> =
        { InternalUsageEvent.InternalResourceId = saasId |> SaaSSubscriptionID.create |> InternalResourceId.SaaSSubscription
          Quantity = quantity |> Quantity.createFloat
          Timestamp = MeteringDateTime.now(); MeterName = meterName |> ApplicationInternalMeterName.create; Properties = None
        }
        |> SubmitUsage eventHubProducerClient cancellationToken
