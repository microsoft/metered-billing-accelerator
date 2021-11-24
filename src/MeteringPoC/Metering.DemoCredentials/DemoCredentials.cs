namespace Metering
{
    using Azure.Core;
    using Azure.Identity;
    using Azure.Messaging.EventHubs;
    using Azure.Messaging.EventHubs.Consumer;
    using Azure.Messaging.EventHubs.Producer;
    using Azure.Storage.Blobs;
    using Metering.Types;
    using Metering.Types.EventHub;
    
    public record AzureEventHubInformation(string EventHubNamespaceName, string EventHubInstanceName, string ConsumerGroup);

    public record DemoCredential(
        TokenCredential InfraCredential, 
        AzureEventHubInformation EventHubInformation, 
        string CheckpointStorageURL, 
        string SnapshotStorageURL, 
        MeteringAPICredentials MeteringAPICredentials);

    public static class DemoCredentials
    {
        private static string GetVar(string name) => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);

        public static string SomeValidSaaSSubscriptionID = "fdc778a6-1281-40e4-cade-4a5fc11f5440";

        public static DemoCredential Get(string consumerGroupName) => new(
            MeteringAPICredentials: MeteringAPICredentialsModule.createServicePrincipal(
                tenantId: GetVar("AZURE_METERING_MARKETPLACE_TENANT_ID"),
                clientId: GetVar("AZURE_METERING_MARKETPLACE_CLIENT_ID"),
                clientSecret: GetVar("AZURE_METERING_MARKETPLACE_CLIENT_SECRET")),
            InfraCredential: new ClientSecretCredential(
                tenantId: GetVar("AZURE_METERING_INFRA_TENANT_ID"),  
                clientId: GetVar("AZURE_METERING_INFRA_CLIENT_ID"),
                clientSecret: GetVar("AZURE_METERING_INFRA_CLIENT_SECRET")),
            EventHubInformation: new(
                EventHubNamespaceName: GetVar("AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME"),
                EventHubInstanceName: GetVar("AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME"),
                ConsumerGroup: consumerGroupName),
            CheckpointStorageURL: GetVar("AZURE_METERING_INFRA_CHECKPOINTS_CONTAINER"),
            SnapshotStorageURL: GetVar("AZURE_METERING_INFRA_SNAPSHOTS_CONTAINER")
            );

        private static string GetNamespace(this DemoCredential config) => $"{config.EventHubInformation.EventHubNamespaceName}.servicebus.windows.net";

        public static BlobContainerClient GetSnapshotStorage(this DemoCredential config) => new(
            blobContainerUri: new Uri(config.SnapshotStorageURL),
            credential: config.InfraCredential);

        public static BlobContainerClient GetCheckpointStorage(this DemoCredential config) => new(
            blobContainerUri: new Uri(config.CheckpointStorageURL),
            credential: config.InfraCredential);

        public static EventHubConnectionDetails GetEventHubConnectionDetails(this DemoCredential config) => new(
            credential: config.InfraCredential,
            eventHubNamespace: config.GetNamespace(),
            eventHubName: config.EventHubInformation.EventHubInstanceName,
            consumerGroupName: config.EventHubInformation.ConsumerGroup,
            checkpointStorage: config.GetCheckpointStorage());
        
        public static EventHubConsumerClient CreateEventHubConsumerClient(this DemoCredential config) => new(
            consumerGroup: config.EventHubInformation.ConsumerGroup,
            fullyQualifiedNamespace: config.GetNamespace(),
            eventHubName: config.EventHubInformation.EventHubInstanceName,
            credential: config.InfraCredential);

        public static EventHubProducerClient CreateEventHubProducerClient(this DemoCredential config) => new(
            fullyQualifiedNamespace: config.GetNamespace(),
            eventHubName: config.EventHubInformation.EventHubInstanceName,
            credential: config.InfraCredential,
            clientOptions: new()
            {
                ConnectionOptions = new()
                {
                    TransportType = EventHubsTransportType.AmqpTcp,
                },
            });

        public static EventProcessorClient CreateEventHubProcessorClient(this DemoCredential config) =>
            EventHubConnectionDetailsModule.createProcessor(config.GetEventHubConnectionDetails());
    }
}