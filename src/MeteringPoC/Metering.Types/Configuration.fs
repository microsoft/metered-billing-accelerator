namespace Metering.Types

open System
open System.Runtime.CompilerServices
open Azure.Core
open Azure.Identity
open Azure.Storage.Blobs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Producer
open Azure.Messaging.EventHubs
open Microsoft.Extensions.Configuration

type ServicePrincipalCredential = 
    { clientId : string 
      clientSecret : string
      tenantId : string }

type MeteringAPICredentials =
    | ManagedIdentity
    | ServicePrincipalCredential of ServicePrincipalCredential

module MeteringAPICredentials =
    let createServicePrincipal tenantId clientId clientSecret =
        { clientId = clientId
          clientSecret = clientSecret
          tenantId = tenantId } |> ServicePrincipalCredential

type AzureEventHubInformation =
    { EventHubNamespaceName: string 
      EventHubInstanceName: string
      ConsumerGroup: string }

type EventHubConnectionDetails =
    { Credential: TokenCredential 
      EventHubNamespace: string
      EventHubName: string
      ConsumerGroupName: string
      CheckpointStorage: BlobContainerClient }
            
type DemoCredential =
    { MeteringAPICredentials: MeteringAPICredentials 
      InfraCredential: TokenCredential 
      EventHubInformation: AzureEventHubInformation
      CheckpointStorageURL: string
      SnapshotStorageURL: string }


[<Extension>]
module DemoCredential =
    let private get_var name =
        Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)

    let GetNamespace (config: DemoCredential) =
        $"{config.EventHubInformation.EventHubNamespaceName}.servicebus.windows.net"

    [<Extension>]
    let GetSnapshotStorage (config: DemoCredential) = 
        new BlobContainerClient(
            blobContainerUri = new Uri(config.SnapshotStorageURL),
            credential = config.InfraCredential)

    [<Extension>]
    let GetCheckpointStorage (config: DemoCredential) = 
        new BlobContainerClient(
            blobContainerUri = new Uri(config.CheckpointStorageURL),
            credential = config.InfraCredential)

    [<Extension>]
    let GetEventHubConnectionDetails (config: DemoCredential) =
        { Credential = config.InfraCredential 
          EventHubNamespace = config |> GetNamespace
          EventHubName = config.EventHubInformation.EventHubInstanceName
          ConsumerGroupName = config.EventHubInformation.ConsumerGroup
          CheckpointStorage = config |> GetCheckpointStorage }
    
    [<Extension>]
    let CreateEventHubConsumerClient (config: DemoCredential) =
        new EventHubConsumerClient (
            consumerGroup = config.EventHubInformation.ConsumerGroup,
            fullyQualifiedNamespace = (config |> GetNamespace),
            eventHubName = config.EventHubInformation.EventHubInstanceName,
            credential = config.InfraCredential);

    [<Extension>]
    let CreateEventHubProducerClient (config: DemoCredential) =
        let connectionOptions = new EventHubConnectionOptions()
        connectionOptions.TransportType <- EventHubsTransportType.AmqpTcp

        let clientOptions = new EventHubProducerClientOptions()
        clientOptions.ConnectionOptions <- connectionOptions

        new EventHubProducerClient(
            fullyQualifiedNamespace = (config |> GetNamespace),
            eventHubName = config.EventHubInformation.EventHubInstanceName,
            credential = config.InfraCredential,
            clientOptions = clientOptions)

    [<Extension>]
    let CreateEventHubProcessorClient(config: DemoCredential) =
        let eventHubConnectionDetails = config |> GetEventHubConnectionDetails

        let clientOptions = new EventProcessorClientOptions()
        clientOptions.TrackLastEnqueuedEventProperties <- true
        clientOptions.PrefetchCount <- 100

        new EventProcessorClient(
            checkpointStore = eventHubConnectionDetails.CheckpointStorage,
            consumerGroup = eventHubConnectionDetails.ConsumerGroupName,
            fullyQualifiedNamespace = eventHubConnectionDetails.EventHubNamespace,
            eventHubName = eventHubConnectionDetails.EventHubName,
            credential = eventHubConnectionDetails.Credential,
            clientOptions = clientOptions)
    
    [<Extension>]
    let getFromConfig (cfg: IConfigurationRoot) (consumerGroupName: string) : DemoCredential =
        { MeteringAPICredentials = 
            { clientId = cfg.Item("MARKETPLACE_CLIENT_ID")
              clientSecret = cfg.Item("MARKETPLACE_CLIENT_SECRET")
              tenantId = cfg.Item("MARKETPLACE_TENANT_ID") }
            |> ServicePrincipalCredential
          InfraCredential = new ClientSecretCredential(
              tenantId = (cfg.Item("INFRA_TENANT_ID")),  
              clientId = (cfg.Item("INFRA_CLIENT_ID")),
              clientSecret = (cfg.Item("INFRA_CLIENT_SECRET")))
          EventHubInformation = 
            { EventHubNamespaceName = cfg.Item("INFRA_EVENTHUB_NAMESPACENAME")
              EventHubInstanceName = cfg.Item("INFRA_EVENTHUB_INSTANCENAME")
              ConsumerGroup = consumerGroupName }
          CheckpointStorageURL = cfg.Item("INFRA_CHECKPOINTS_CONTAINER")
          SnapshotStorageURL = cfg.Item("INFRA_SNAPSHOTS_CONTAINER") }

    let getFromEnvironment (consumerGroupName: string) : DemoCredential =
        let builder = new ConfigurationBuilder()
        let builder = EnvironmentVariablesExtensions.AddEnvironmentVariables(builder, prefix = "AZURE_METERING_")
        let configuration = builder.Build()
        getFromConfig configuration EventHubConsumerClient.DefaultConsumerGroupName
