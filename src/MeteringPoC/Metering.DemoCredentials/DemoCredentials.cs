namespace Metering
{
    using Azure.Core;
    using Azure.Identity;
    using Azure.Storage.Blobs;
    using Metering.Types.EventHub;
    using System.IO;

    public record StorageContainerInformation(string StorageAccountName, string StorageContainerName);

    public record AzureEventHubInformation(string EventHubNamespaceName, string EventHubInstanceName, string ConsumerGroup);

    public record DemoCredential(TokenCredential TokenCredential, AzureEventHubInformation EventHubInformation, StorageContainerInformation CheckpointStorage, StorageContainerInformation SnapshotStorage);

    public static class DemoCredentials
    {
        public static DemoCredential Get(string consumerGroupName) => new(
                TokenCredential: new ClientSecretCredential(
                    tenantId: "chgeuerfte.onmicrosoft.com",
                    clientId: "9fc61765-75a9-4aef-80d1-83b052e58b42",
                    clientSecret: File.ReadAllText(@"C:\Users\chgeuer\Desktop\Metering Hack\client_secret").Trim()),
                EventHubInformation: new(
                    EventHubNamespaceName: "meteringhack-standard",
                    EventHubInstanceName: "hub1",
                    ConsumerGroup: consumerGroupName),
                CheckpointStorage: new(
                    StorageAccountName: "meteringhack",
                    StorageContainerName: "checkpoint"),
                SnapshotStorage: new(
                    StorageAccountName: "meteringhack",
                    StorageContainerName: "snapshots")
                );

        public static BlobContainerClient GetSnapshotStorage(this DemoCredential config) => new(
                blobContainerUri: new Uri($"https://{config.SnapshotStorage.StorageAccountName}.blob.core.windows.net/{config.SnapshotStorage.StorageContainerName}/"),
                credential: config.TokenCredential);

        public static BlobContainerClient GetCheckpointStorage(this DemoCredential config) => new(
                blobContainerUri: new Uri($"https://{config.CheckpointStorage.StorageAccountName}.blob.core.windows.net/{config.CheckpointStorage.StorageContainerName}/"),
                credential: config.TokenCredential);

        public static EventHubConnectionDetails GetEventHubConnectionDetails(this DemoCredential config) => new(
                credential: config.TokenCredential,
                eventHubNamespace: $"{config.EventHubInformation.EventHubNamespaceName}.servicebus.windows.net",
                eventHubName: config.EventHubInformation.EventHubInstanceName,
                consumerGroupName: config.EventHubInformation.ConsumerGroup,
                checkpointStorage: config.GetCheckpointStorage());
    }
}