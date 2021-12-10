namespace Metering.ClientSDK

open System
open System.Threading
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Producer
open Metering.Types

type MeterValue = 
    { Quantity: Quantity
      Name: ApplicationInternalMeterName }

module MeterValue =
    [<CompiledName("create")>]
    let createInt (name: string) (quantity: uint64) : MeterValue =
        { Quantity = quantity |> Quantity.createInt
          Name = name |> ApplicationInternalMeterName.create }

    [<CompiledName("create")>]
    let createFloat (name: string) (quantity: decimal) : MeterValue =
        { Quantity = quantity |> Quantity.createFloat
          Name = name |> ApplicationInternalMeterName.create }        

type ManagedAppConsumption = 
    Meters of MeterValue list

module ManagedAppConsumption =
    let create ([<ParamArray>] vals: MeterValue array) =
        vals |> Array.toList |> Meters
        
type SaaSConsumption = { 
    SaaSSubscriptionID: SaaSSubscriptionID 
    Meters: MeterValue list }

module SaaSConsumption =
    let create (saasId: string) ([<ParamArray>] vals: MeterValue array) =
        { SaaSSubscriptionID = saasId |> SaaSSubscriptionID.create
          Meters = vals |> Array.toList }

[<Extension>]
module MeteringEventHubExtensions =    
    // [<Extension>] // Currently not exposed to C#
    let private SubmitMeteringUpdateEvent (eventHubProducerClient: EventHubProducerClient) (cancellationToken: CancellationToken) (meteringUpdateEvents: MeteringUpdateEvent list) : Task =
        task {
            // the public functions and type design in this module ensure all events from a call go to the same partition
            let partitionKey = 
                meteringUpdateEvents
                |> List.head
                |> MeteringUpdateEvent.partitionKey

            let! eventBatch = eventHubProducerClient.CreateBatchAsync(
                options = new CreateBatchOptions (PartitionKey = partitionKey),
                cancellationToken = cancellationToken)
            
            meteringUpdateEvents
            |> List.map (Json.toStr 0)
            |> List.map (fun x -> new BinaryData(x))
            |> List.map (fun x -> new EventData(x))
            |> List.iter (fun x -> 
                x.Properties.Add("SendingApplication", (System.Reflection.Assembly.GetEntryAssembly().FullName))
                if not (eventBatch.TryAdd(x))
                then failwith "The event could not be added."
                else ()
            )

            return! eventHubProducerClient.SendAsync(
                eventBatch = eventBatch,
                // options: new SendEventOptions() {  PartitionId = "...", PartitionKey = "..." },
                cancellationToken = cancellationToken)
        }


    [<Extension>]
    [<CompiledName("SubmitManagedAppMeterAsync")>]
    let SubmitManagedAppFloatAsync (eventHubProducerClient: EventHubProducerClient) ({Meters = consumption}) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        consumption
        |> List.map(fun v -> 
            { InternalUsageEvent.InternalResourceId = InternalResourceId.ManagedApp
              Quantity = v.Quantity
              Timestamp = MeteringDateTime.now()
              MeterName = v.Name
              Properties = None } 
            |> UsageReported
        )
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken

    [<Extension>]
    [<CompiledName("SubmitSaaSMeterAsync")>] // Naming these for C# method overloading
    let SubmitSaasIntegerAsync (eventHubProducerClient: EventHubProducerClient) (meter: SaaSConsumption) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        meter.Meters
        |> List.map (fun v -> 
            { InternalUsageEvent.InternalResourceId = meter.SaaSSubscriptionID |> InternalResourceId.SaaSSubscription
              Quantity = v.Quantity
              Timestamp = MeteringDateTime.now()
              MeterName = v.Name
              Properties = None }
             |> UsageReported
        )
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken
        
    [<Extension>]
    [<CompiledName("SubmitSubscriptionCreationAsync")>]
    let SubmitSubscriptionCreation (eventHubProducerClient: EventHubProducerClient) (sci: SubscriptionCreationInformation) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        [SubscriptionPurchased sci]
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken

    // this is not exposed as C# extension, as only F# is supposed to call it. 
    let ReportUsageSubmitted (eventHubProducerClient: EventHubProducerClient) (msr: MarketplaceSubmissionResult) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
        [UsageSubmittedToAPI msr]
        |> SubmitMeteringUpdateEvent eventHubProducerClient cancellationToken
