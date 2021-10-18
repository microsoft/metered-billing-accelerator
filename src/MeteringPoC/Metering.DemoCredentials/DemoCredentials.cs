namespace Metering
{
    using Azure.Core;
    using Azure.Identity;
    using System.IO;

    public record StorageContainerInformation(string StorageAccountName, string StorageContainerName);

    public record AzureEventHubInformation(string EventHubNamespaceName, string EventHubInstanceName, string ConsumerGroup);

    public record DemoCredential(TokenCredential TokenCredential, AzureEventHubInformation EventHubInformation, StorageContainerInformation CheckpointStorage);

    public static class DemoCredentials
    {
        public static DemoCredential Get(string consumerGroupName) => new(
                TokenCredential: new ClientSecretCredential(
                    tenantId: "chgeuerfte.onmicrosoft.com",
                    clientId: "9fc61765-75a9-4aef-80d1-83b052e58b42",
                    clientSecret: File.ReadAllText(@"..\..\..\..\..\client_secret").Trim()),
                EventHubInformation: new(
                    EventHubNamespaceName: "meteringhack-standard",
                    EventHubInstanceName: "hub1",
                    ConsumerGroup: consumerGroupName),
                CheckpointStorage: new(
                    StorageAccountName: "meteringhack",
                    StorageContainerName: "checkpoint"));
    }
}