namespace Metering

open System
open System.Threading
open System.Runtime.CompilerServices
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Producer
open Metering.Types

[<Extension>]
module MeteringEventHubExtensions =
    let private SubmitUsage (producer: EventHubProducerClient) (ct: CancellationToken) (internalUsageEvent: InternalUsageEvent) = task {
        
        // PartitionKey = Guid.NewGuid().ToString(),
        // PartitionId = ids[id],
        let! eventBatch = producer.CreateBatchAsync((new CreateBatchOptions()), cancellationToken = ct)
        
        let eventData = new EventData(new BinaryData(Json.toStr(internalUsageEvent)))

        //eventData.Properties.Add("SendingApplication", typeof(EventHubDemoProgram).Assembly.Location);
        if not (eventBatch.TryAdd(eventData))
        then failwith "The event could not be added."
        else ()

        // options: new SendEventOptions() {  PartitionId = "...", PartitionKey = "..." },
        return! (producer.SendAsync(eventBatch, cancellationToken = ct))
    }

    [<Extension>]
    let SubmitManagedAppIntegerAsync (producer: EventHubProducerClient) (meterName: string) (v: uint64) (ct: CancellationToken) =
        { InternalUsageEvent.InternalResourceId = InternalResourceId.ManagedApp
          Quantity = v |> Quantity.createInt 
          Timestamp = MeteringDateTime.now(); MeterName = meterName |> ApplicationInternalMeterName.create; Properties = None
        }
        |> SubmitUsage producer ct
    
    [<Extension>]
    let SubmitManagedAppFloatAsync (producer: EventHubProducerClient) (meterName: string) (v: decimal) (ct: CancellationToken) =
        { InternalUsageEvent.InternalResourceId = InternalResourceId.ManagedApp
          Quantity = v |> Quantity.createFloat
          Timestamp = MeteringDateTime.now(); MeterName = meterName |> ApplicationInternalMeterName.create; Properties = None
        }
        |> SubmitUsage producer ct

    [<Extension>]
    let SubmitSaasIntegerAsync (producer: EventHubProducerClient) (saasId: string) (meterName: string) (v: uint64) (ct: CancellationToken) =
        { InternalUsageEvent.InternalResourceId = saasId |> SaaSSubscriptionID.create |> InternalResourceId.SaaSSubscription
          Quantity = v |> Quantity.createInt 
          Timestamp = MeteringDateTime.now(); MeterName = meterName |> ApplicationInternalMeterName.create; Properties = None
        }
        |> SubmitUsage producer ct

    [<Extension>]
    let SubmitSaasFloatAsync (producer: EventHubProducerClient) (saasId: string) (meterName: string) (v: decimal) (ct: CancellationToken) =
        { InternalUsageEvent.InternalResourceId = saasId |> SaaSSubscriptionID.create |> InternalResourceId.SaaSSubscription
          Quantity = v |> Quantity.createFloat
          Timestamp = MeteringDateTime.now(); MeterName = meterName |> ApplicationInternalMeterName.create; Properties = None
        }
        |> SubmitUsage producer ct
