// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Integration

open System
open System.Runtime.CompilerServices
open Microsoft.Extensions.Configuration
open Azure.Core
open Azure.Storage.Blobs
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Producer
open Metering.EventHub

type MeteringConnections =
    { MeteringAPICredentials: MeteringAPICredentials 
      SnapshotStorage: BlobContainerClient      
      EventHubConfig: EventHubConfig }

[<Extension>]
module MeteringConnections =
    let private environmentVariablePrefix = "AZURE_METERING_"

    let getConfiguration () : (string -> string option) = 
        // Doing this convoluted syntax as c# extension methods seem unavailable.
        let configuration = EnvironmentVariablesExtensions.AddEnvironmentVariables(
            new ConfigurationBuilder(),
            prefix = environmentVariablePrefix).Build()
        let get key = 
            match configuration.Item(key) with
            | v when String.IsNullOrWhiteSpace(v) -> None
            | v -> Some v
        get

    let private getRequired get var =
        match var |> get with
        | Some s -> s
        | None -> failwith $"Missing configuration {environmentVariablePrefix}{var}"

    let private getInfraStructureCredential (get: (string -> string option)) : TokenCredential = 
        match ("INFRA_TENANT_ID" |> get, "INFRA_CLIENT_ID" |> get, "INFRA_CLIENT_SECRET" |> get) with
        | (Some t, Some i, Some s) -> InfraStructureCredentials.createServicePrincipal t i s
        | (None, None, None) -> InfraStructureCredentials.createManagedIdentity()
        | _ -> failwith $"The {nameof(InfraStructureCredentials)} configuration is incomplete."

    let private getEventHubName get =
         EventHubName.create
            ("INFRA_EVENTHUB_NAMESPACENAME" |> getRequired get)
            ("INFRA_EVENTHUB_INSTANCENAME" |> getRequired get)

    let private getMeteringApiCredential get = 
        match ("MARKETPLACE_TENANT_ID" |> get, "MARKETPLACE_CLIENT_ID" |> get, "MARKETPLACE_CLIENT_SECRET" |> get) with
        | (Some t, Some i, Some s) -> MeteringAPICredentials.createServicePrincipal t i s
        | (None, None, None) -> ManagedIdentity
        | _ -> failwith $"The {nameof(MeteringAPICredentials)} configuration is incomplete."

    let private getFromConfig (get: (string -> string option)) (consumerGroupName: string) =
        let containerClientWith (cred: TokenCredential) uri = new BlobContainerClient(blobContainerUri = new Uri(uri), credential = cred)

        let captureStorage =
            match ("INFRA_CAPTURE_CONTAINER" |> get, "INFRA_CAPTURE_FILENAME_FORMAT" |> get) with
            | (None, None) -> None
            | (Some c, Some f) -> 
                { Storage = c |> containerClientWith (getInfraStructureCredential get)
                  CaptureFileNameFormat = f }
                |> Some
            |  _ -> failwith $"The {nameof(CaptureStorage)} configuration is incomplete."

        let infraCred = getInfraStructureCredential get

        { MeteringAPICredentials = getMeteringApiCredential get
          SnapshotStorage = "INFRA_SNAPSHOTS_CONTAINER" |> getRequired get |> containerClientWith infraCred
          EventHubConfig = 
            { CheckpointStorage = "INFRA_CHECKPOINTS_CONTAINER" |> getRequired get |> containerClientWith infraCred
              CaptureStorage = captureStorage
              ConsumerGroupName = consumerGroupName
              EventHubName = getEventHubName get
              InfraStructureCredentials = infraCred } }

    let getFromEnvironmentWithSpecificConsumerGroup (consumerGroupName: string) =
        getFromConfig (getConfiguration()) consumerGroupName

    let getFromEnvironment () = 
        getFromEnvironmentWithSpecificConsumerGroup EventHubConsumerClient.DefaultConsumerGroupName
    
    [<Extension>]
    let createEventHubConsumerClient (connections: MeteringConnections) : EventHubConsumerClient =
        let eh = connections.EventHubConfig
        new EventHubConsumerClient(
            consumerGroup = eh.ConsumerGroupName,
            fullyQualifiedNamespace = eh.EventHubName.FullyQualifiedNamespace,
            eventHubName = eh.EventHubName.InstanceName,
            credential = eh.InfraStructureCredentials)

    [<Extension>]
    let createEventProcessorClient (connections: MeteringConnections) : EventProcessorClient =
        let eh = connections.EventHubConfig
        new EventProcessorClient(
            checkpointStore = eh.CheckpointStorage,
            consumerGroup = eh.ConsumerGroupName,
            fullyQualifiedNamespace = eh.EventHubName.FullyQualifiedNamespace,
            eventHubName = eh.EventHubName.InstanceName,
            credential = eh.InfraStructureCredentials,
            clientOptions = new EventProcessorClientOptions(
                TrackLastEnqueuedEventProperties = true,
                PartitionOwnershipExpirationInterval = TimeSpan.FromMinutes(1),
                PrefetchCount = 1000,
                CacheEventCount = 5000))

    [<Extension>]
    [<CompiledName("createEventHubProducerClient")>]
    let createEventHubProducerClient (connections: MeteringConnections) : EventHubProducerClient =
        let eh = connections.EventHubConfig
        new EventHubProducerClient(
            fullyQualifiedNamespace = eh.EventHubName.FullyQualifiedNamespace,
            eventHubName = eh.EventHubName.InstanceName,
            credential = eh.InfraStructureCredentials,
            clientOptions = new EventHubProducerClientOptions(
                ConnectionOptions = new EventHubConnectionOptions(
                    TransportType = EventHubsTransportType.AmqpTcp)))

    let createEventHubProducerClientForClientSDK (): EventHubProducerClient =
        let get = getConfiguration() 
        let eh = getEventHubName get
        let infraCred = getInfraStructureCredential get
        
        new EventHubProducerClient(
            fullyQualifiedNamespace = eh.FullyQualifiedNamespace,
            eventHubName = eh.InstanceName,
            credential = infraCred,
            clientOptions = new EventHubProducerClientOptions(
                ConnectionOptions = new EventHubConnectionOptions(
                    TransportType = EventHubsTransportType.AmqpTcp)))
