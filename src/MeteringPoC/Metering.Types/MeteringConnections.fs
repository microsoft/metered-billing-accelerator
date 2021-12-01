﻿namespace Metering.Types

open System
open Microsoft.Extensions.Configuration
open Azure.Core
open Azure.Storage.Blobs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Producer
open Azure.Messaging.EventHubs

type MeteringConnections =
    { MeteringAPICredentials: MeteringAPICredentials 
      EventHubConsumerClient: EventHubConsumerClient
      EventHubProducerClient: EventHubProducerClient
      EventProcessorClient: EventProcessorClient
      SnapshotStorage: BlobContainerClient }


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

        let checkpointStorage = "INFRA_CHECKPOINTS_CONTAINER" |> getRequired |> containerClientWith infraStructureCredential
        let snapStorage = "INFRA_SNAPSHOTS_CONTAINER" |> getRequired |> containerClientWith infraStructureCredential

        let instanceName = "INFRA_EVENTHUB_INSTANCENAME" |> getRequired
        let nsn = "INFRA_EVENTHUB_NAMESPACENAME" |> getRequired
        let fullyQualifiedNamespace = $"{nsn}.servicebus.windows.net"

        let processorClient = 
            let clientOptions = new EventProcessorClientOptions()
            clientOptions.TrackLastEnqueuedEventProperties <- true
            clientOptions.PrefetchCount <- 100
            new EventProcessorClient(
                checkpointStore = checkpointStorage,
                consumerGroup = consumerGroupName,
                fullyQualifiedNamespace = fullyQualifiedNamespace,
                eventHubName = instanceName,
                credential = infraStructureCredential,
                clientOptions = clientOptions)

        let consumerClient = 
            new EventHubConsumerClient (
                consumerGroup = consumerGroupName,
                fullyQualifiedNamespace = fullyQualifiedNamespace,
                eventHubName = instanceName,
                credential = infraStructureCredential)

        let producerClient =
            let connectionOptions = new EventHubConnectionOptions()
            connectionOptions.TransportType <- EventHubsTransportType.AmqpTcp
            let producerClientOptions = new EventHubProducerClientOptions()
            producerClientOptions.ConnectionOptions <- connectionOptions
            new EventHubProducerClient(
                fullyQualifiedNamespace = fullyQualifiedNamespace,
                eventHubName = instanceName,
                credential = infraStructureCredential,
                clientOptions = producerClientOptions)

        { MeteringAPICredentials = meteringApiCredential
          EventHubConsumerClient = consumerClient
          EventHubProducerClient = producerClient
          EventProcessorClient = processorClient
          SnapshotStorage = snapStorage }

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