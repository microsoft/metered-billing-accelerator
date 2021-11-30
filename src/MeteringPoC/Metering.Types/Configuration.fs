namespace Metering.Types

open System
open Microsoft.Extensions.Configuration
open Azure.Core
open Azure.Identity
open Azure.Storage.Blobs
open Azure.Messaging.EventHubs.Consumer
open Azure.Messaging.EventHubs.Producer
open Azure.Messaging.EventHubs

type ServicePrincipalCredential = 
    { ClientId: string 
      ClientSecret: string
      TenantId: string }

type MeteringAPICredentials =
    | ManagedIdentity
    | ServicePrincipalCredential of ServicePrincipalCredential

module MeteringAPICredentials =
    let createServicePrincipal tenantId clientId clientSecret =
        { ClientId = clientId
          ClientSecret = clientSecret
          TenantId = tenantId } |> ServicePrincipalCredential

type MeteringConnections =
    { MeteringAPICredentials: MeteringAPICredentials 
      EventHubConsumerClient: EventHubConsumerClient
      EventHubProducerClient: EventHubProducerClient
      EventProcessorClient: EventProcessorClient
      SnapshotStorage: BlobContainerClient }

module MeteringConnections =
    let private environmentVariablePrefix = "AZURE_METERING_"

    let private getFromConfig (get: (string -> string)) (consumerGroupName: string) =
        let meteringApiCredential = 
            { ClientId = "MARKETPLACE_CLIENT_ID" |> get
              ClientSecret = "MARKETPLACE_CLIENT_SECRET" |> get
              TenantId = "MARKETPLACE_TENANT_ID" |> get } |> ServicePrincipalCredential

        let infraStructureCredential = new ClientSecretCredential(
            tenantId = ("INFRA_TENANT_ID"  |> get),  
            clientId = ("INFRA_CLIENT_ID" |> get),
            clientSecret = ("INFRA_CLIENT_SECRET" |> get))

        let containerClientWithCredential (tokenCred: TokenCredential) uri = new BlobContainerClient(blobContainerUri = new Uri(uri), credential = tokenCred)
        let checkpointStorage  = "INFRA_CHECKPOINTS_CONTAINER" |> get |> containerClientWithCredential infraStructureCredential
        let snapStorage = "INFRA_SNAPSHOTS_CONTAINER" |> get |> containerClientWithCredential infraStructureCredential

        let instanceName = "INFRA_EVENTHUB_INSTANCENAME" |> get
        let nsn = "INFRA_EVENTHUB_NAMESPACENAME" |> get
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

    let getFromEnvironment (consumerGroupName: string) =
        let configuration =
            EnvironmentVariablesExtensions.AddEnvironmentVariables(
                new ConfigurationBuilder(),
                prefix = environmentVariablePrefix).Build()

        getFromConfig
            (fun name -> configuration.Item(name))
            EventHubConsumerClient.DefaultConsumerGroupName
