namespace Metering.Types

open System
open Microsoft.Extensions.Configuration
open Azure.Core
open Azure.Storage.Blobs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Producer
open Azure.Messaging.EventHubs
open System.Runtime.CompilerServices

type EventHubConfig =
    { NamespaceName: string
      FullyQualifiedNamespace: string
      InstanceName: string
      ConsumerGroupName: string
      CheckpointStorage: BlobContainerClient
      InfraStructureCredentials: TokenCredential }

type MeteringConnections =
    { MeteringAPICredentials: MeteringAPICredentials 
      SnapshotStorage: BlobContainerClient
      EventHubConfig: EventHubConfig }

[<Extension>]
module MeteringConnections =
    let private environmentVariablePrefix = "AZURE_METERING_"

    let private getFromConfig (get: (string -> string option)) (consumerGroupName: string) =
        let getRequired var =
            match var |> get with
            | Some s -> s
            | None -> failwith $"Missing configuration {environmentVariablePrefix}{var}"

        let meteringApiCredential = 
            match ("MARKETPLACE_TENANT_ID" |> get, "MARKETPLACE_CLIENT_ID" |> get, "MARKETPLACE_CLIENT_SECRET" |> get) with
            | (Some t, Some i, Some s) -> MeteringAPICredentials.createServicePrincipal t i s
            | (None, None, None) -> ManagedIdentity
            | _ -> failwith "The MeteringAPICredential configuration is incomplete."
            
        let infraStructureCredential = 
            match ("INFRA_TENANT_ID" |> get, "INFRA_CLIENT_ID" |> get, "INFRA_CLIENT_SECRET" |> get) with
            | (Some t, Some i, Some s) -> InfraStructureCredentials.createServicePrincipal t i s
            | (None, None, None) -> InfraStructureCredentials.createManagedIdentity()
            | _ -> failwith "The InfraStructureCredential configuration is incomplete."
                    
        let containerClientWith (cred: TokenCredential) uri = new BlobContainerClient(blobContainerUri = new Uri(uri), credential = cred)

        let nsn =  "INFRA_EVENTHUB_NAMESPACENAME" |> getRequired

        { MeteringAPICredentials = meteringApiCredential
          SnapshotStorage = "INFRA_SNAPSHOTS_CONTAINER" |> getRequired |> containerClientWith infraStructureCredential
          EventHubConfig = 
            { CheckpointStorage = "INFRA_CHECKPOINTS_CONTAINER" |> getRequired |> containerClientWith infraStructureCredential 
              ConsumerGroupName = consumerGroupName
              NamespaceName = nsn
              FullyQualifiedNamespace = $"{nsn}.servicebus.windows.net"
              InstanceName =  "INFRA_EVENTHUB_INSTANCENAME" |> getRequired
              InfraStructureCredentials = infraStructureCredential } }

    let getFromEnvironmentWithSpecificConsumerGroup (consumerGroupName: string) =
        let configuration =
            // Doing this convoluted syntax as c# extension methods seem unavailable.
            EnvironmentVariablesExtensions.AddEnvironmentVariables(
                new ConfigurationBuilder(),
                prefix = environmentVariablePrefix).Build()

        let get key = 
            match configuration.Item(key) with
            | v when String.IsNullOrWhiteSpace(v) -> None
            | v -> Some v

        getFromConfig get EventHubConsumerClient.DefaultConsumerGroupName

    let getFromEnvironment () =
        getFromEnvironmentWithSpecificConsumerGroup EventHubConsumerClient.DefaultConsumerGroupName
    
    [<Extension>]
    let createEventHubConsumerClient (connections: MeteringConnections) : EventHubConsumerClient =
        new EventHubConsumerClient(
            consumerGroup = connections.EventHubConfig.ConsumerGroupName,
            fullyQualifiedNamespace = connections.EventHubConfig.FullyQualifiedNamespace,
            eventHubName = connections.EventHubConfig.InstanceName,
            credential = connections.EventHubConfig.InfraStructureCredentials)

    [<Extension>]
    let createEventHubProducerClient (connections: MeteringConnections) : EventHubProducerClient =
        let connectionOptions = new EventHubConnectionOptions()
        connectionOptions.TransportType <- EventHubsTransportType.AmqpTcp
        let producerClientOptions = new EventHubProducerClientOptions()
        producerClientOptions.ConnectionOptions <- connectionOptions
        new EventHubProducerClient(
            fullyQualifiedNamespace = connections.EventHubConfig.FullyQualifiedNamespace,
            eventHubName = connections.EventHubConfig.InstanceName,
            credential = connections.EventHubConfig.InfraStructureCredentials,
            clientOptions = producerClientOptions)

    [<Extension>]
    let createEventProcessorClient (connections: MeteringConnections) : EventProcessorClient =
        let clientOptions = new EventProcessorClientOptions()
        clientOptions.TrackLastEnqueuedEventProperties <- true
        clientOptions.PartitionOwnershipExpirationInterval <- TimeSpan.FromMinutes(1)        
        clientOptions.PrefetchCount <- 100
        new EventProcessorClient(
            checkpointStore = connections.EventHubConfig.CheckpointStorage,
            consumerGroup = connections.EventHubConfig.ConsumerGroupName,
            fullyQualifiedNamespace = connections.EventHubConfig.FullyQualifiedNamespace,
            eventHubName = connections.EventHubConfig.InstanceName,
            credential = connections.EventHubConfig.InfraStructureCredentials,
            clientOptions = clientOptions)
