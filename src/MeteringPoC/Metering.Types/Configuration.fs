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
      //InfraCredential: TokenCredential 
      EventHubConsumerClient: EventHubConsumerClient
      EventHubProducerClient: EventHubProducerClient
      EventProcessorClient: EventProcessorClient
      CheckpointStorage: BlobContainerClient
      SnapshotStorage: BlobContainerClient }


module DemoCredential =
    let private getFromConfig (cfg: IConfigurationRoot) (consumerGroupName: string) : DemoCredential =
        let x n = cfg.Item(n)

        let meteringApiCredential = 
            { clientId = "MARKETPLACE_CLIENT_ID" |> x
              clientSecret = "MARKETPLACE_CLIENT_SECRET" |> x
              tenantId = "MARKETPLACE_TENANT_ID" |> x } |> ServicePrincipalCredential

        let infraCred = new ClientSecretCredential(
            tenantId = ("INFRA_TENANT_ID"  |> x),  
            clientId = ("INFRA_CLIENT_ID" |> x),
            clientSecret = ("INFRA_CLIENT_SECRET" |> x))

        let bc (c: TokenCredential) u = new BlobContainerClient(blobContainerUri = new Uri(u), credential = c)
        let checkpointStorage  = "INFRA_CHECKPOINTS_CONTAINER" |> x |> bc infraCred
        let snapStorage = "INFRA_SNAPSHOTS_CONTAINER"  |> x |> bc infraCred

        let nsn = "INFRA_EVENTHUB_NAMESPACENAME" |> x
        let fullyQualifiedNamespace = $"{nsn}.servicebus.windows.net"
        let instanceName = "INFRA_EVENTHUB_INSTANCENAME" |> x

        let connectionOptions = new EventHubConnectionOptions()
        connectionOptions.TransportType <- EventHubsTransportType.AmqpTcp
        let producerClientOptions = new EventHubProducerClientOptions()
        producerClientOptions.ConnectionOptions <- connectionOptions
        let eventHubProducerClient = new EventHubProducerClient(
            fullyQualifiedNamespace = fullyQualifiedNamespace,
            eventHubName = fullyQualifiedNamespace,
            credential = infraCred,
            clientOptions = producerClientOptions)

        let clientOptions = new EventProcessorClientOptions()
        clientOptions.TrackLastEnqueuedEventProperties <- true
        clientOptions.PrefetchCount <- 100

        let eventHubConsumerClientOptions = new EventHubConsumerClientOptions()

        let eventHubConsumerClient = new EventHubConsumerClient (
            consumerGroup = consumerGroupName,
            fullyQualifiedNamespace = fullyQualifiedNamespace,
            eventHubName = instanceName,
            credential = infraCred,
            clientOptions = eventHubConsumerClientOptions)

        let eventProcessorClient = new EventProcessorClient(
            checkpointStore = checkpointStorage,
            consumerGroup = consumerGroupName,
            fullyQualifiedNamespace = fullyQualifiedNamespace,
            eventHubName = instanceName,
            credential = infraCred,
            clientOptions = clientOptions)

        { MeteringAPICredentials = meteringApiCredential
          EventHubConsumerClient = eventHubConsumerClient
          EventHubProducerClient = eventHubProducerClient
          EventProcessorClient = eventProcessorClient
          CheckpointStorage = checkpointStorage
          SnapshotStorage = snapStorage }

    let getFromEnvironment (consumerGroupName: string) : DemoCredential =
        let builder = new ConfigurationBuilder()
        let builder = EnvironmentVariablesExtensions.AddEnvironmentVariables(builder, prefix = "AZURE_METERING_")
        let configuration = builder.Build()
        getFromConfig configuration EventHubConsumerClient.DefaultConsumerGroupName
