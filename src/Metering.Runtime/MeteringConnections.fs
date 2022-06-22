// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Integration

open System
open Microsoft.Extensions.Configuration
open Azure.Core
open Azure.Storage.Blobs
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Producer
open Metering.BaseTypes.EventHub

type CaptureStorage = 
    { CaptureFileNameFormat: string
      Storage: BlobContainerClient }

type EventHubConfig =
    { EventHubName: EventHubName
      ConsumerGroupName: string
      CheckpointStorage: BlobContainerClient
      CaptureStorage: CaptureStorage option
      InfraStructureCredentials: TokenCredential }

type MeteringConnections =
    { MeteringAPICredentials: MeteringAPICredentials 
      SnapshotStorage: BlobContainerClient      
      EventHubConfig: EventHubConfig }

    static member private environmentVariablePrefix = "AZURE_METERING_"

    static member getConfiguration () : (string -> string option) = 
        // Doing this convoluted syntax as c# extension methods seem unavailable.
        let configuration = EnvironmentVariablesExtensions.AddEnvironmentVariables(
            new ConfigurationBuilder(),
            prefix = MeteringConnections.environmentVariablePrefix).Build()
        let get key = 
            match configuration.Item(key) with
            | v when String.IsNullOrWhiteSpace(v) -> None
            | v -> Some v
        get

    static member private getRequired get var =
        match var |> get with
        | Some s -> s
        | None -> failwith $"Missing configuration {MeteringConnections.environmentVariablePrefix}{var}"

    static member private getInfraStructureCredential (get: (string -> string option)) : TokenCredential = 
        match ("INFRA_TENANT_ID" |> get, "INFRA_CLIENT_ID" |> get, "INFRA_CLIENT_SECRET" |> get) with
        | (Some t, Some i, Some s) -> InfraStructureCredentials.createServicePrincipal t i s
        | (None, None, None) -> InfraStructureCredentials.createManagedIdentity()
        | _ -> failwith $"The {nameof(InfraStructureCredentials)} configuration is incomplete."

    static member private getEventHubName get =
         EventHubName.create
            ("INFRA_EVENTHUB_NAMESPACENAME" |> MeteringConnections.getRequired get)
            ("INFRA_EVENTHUB_INSTANCENAME" |> MeteringConnections.getRequired get)

    static member private getMeteringApiCredential get = 
        match ("MARKETPLACE_TENANT_ID" |> get, "MARKETPLACE_CLIENT_ID" |> get, "MARKETPLACE_CLIENT_SECRET" |> get) with
        | (Some t, Some i, Some s) -> MeteringAPICredentials.createServicePrincipal t i s
        | (None, None, None) -> ManagedIdentity
        | _ -> failwith $"The {nameof(MeteringAPICredentials)} configuration is incomplete."

    static member private getFromConfig (get: (string -> string option)) (consumerGroupName: string) =
        let containerClientWith (cred: TokenCredential) uri = new BlobContainerClient(blobContainerUri = new Uri(uri), credential = cred)

        let captureStorage =
            match ("INFRA_CAPTURE_CONTAINER" |> get, "INFRA_CAPTURE_FILENAME_FORMAT" |> get) with
            | (None, None) -> None
            | (Some c, Some f) -> 
                { Storage = c |> containerClientWith (MeteringConnections.getInfraStructureCredential get)
                  CaptureFileNameFormat = f }
                |> Some
            |  _ -> failwith $"The {nameof(CaptureStorage)} configuration is incomplete."

        let infraCred = MeteringConnections.getInfraStructureCredential get

        { MeteringAPICredentials = MeteringConnections.getMeteringApiCredential get
          SnapshotStorage = "INFRA_SNAPSHOTS_CONTAINER" |> MeteringConnections.getRequired get |> containerClientWith infraCred
          EventHubConfig = 
            { CheckpointStorage = "INFRA_CHECKPOINTS_CONTAINER" |> MeteringConnections.getRequired get |> containerClientWith infraCred
              CaptureStorage = captureStorage
              ConsumerGroupName = consumerGroupName
              EventHubName = MeteringConnections.getEventHubName get
              InfraStructureCredentials = infraCred } }

    static member  getFromEnvironmentWithSpecificConsumerGroup (consumerGroupName: string) =
        MeteringConnections.getFromConfig (MeteringConnections.getConfiguration()) consumerGroupName

    static member getFromEnvironment() = 
        MeteringConnections.getFromEnvironmentWithSpecificConsumerGroup EventHubConsumerClient.DefaultConsumerGroupName
    
    member this.createEventHubConsumerClient() : EventHubConsumerClient =
        let eh = this.EventHubConfig
        new EventHubConsumerClient(
            consumerGroup = eh.ConsumerGroupName,
            fullyQualifiedNamespace = eh.EventHubName.FullyQualifiedNamespace,
            eventHubName = eh.EventHubName.InstanceName,
            credential = eh.InfraStructureCredentials)

    member this.createEventProcessorClient() : EventProcessorClient =
        let eh = this.EventHubConfig
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

    member this.createEventHubProducerClient() =
        let eh = this.EventHubConfig
        new EventHubProducerClient(
            fullyQualifiedNamespace = eh.EventHubName.FullyQualifiedNamespace,
            eventHubName = eh.EventHubName.InstanceName,
            credential = eh.InfraStructureCredentials,
            clientOptions = new EventHubProducerClientOptions(
                ConnectionOptions = new EventHubConnectionOptions(
                    TransportType = EventHubsTransportType.AmqpTcp)))

    static member createEventHubProducerClientForClientSDK () : EventHubProducerClient =
        let get = MeteringConnections.getConfiguration() 
        let eh = MeteringConnections.getEventHubName get
        let infraCred = MeteringConnections.getInfraStructureCredential get
        
        new EventHubProducerClient(
            fullyQualifiedNamespace = eh.FullyQualifiedNamespace,
            eventHubName = eh.InstanceName,
            credential = infraCred,
            clientOptions = new EventHubProducerClientOptions(
                ConnectionOptions = new EventHubConnectionOptions(
                    TransportType = EventHubsTransportType.AmqpTcp)))
