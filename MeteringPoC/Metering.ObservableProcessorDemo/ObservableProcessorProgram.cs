namespace Metering
{
    using Azure.Messaging.EventHubs.Consumer;
    using Azure.Messaging.EventHubs.Processor;
    using Azure.Storage.Blobs;
    using Metering.Messaging;
    using Metering.Types;
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    class ObservableProcessorProgram
    { 
		static async Task Main()
		{
            Console.Title = nameof(ObservableProcessorProgram);

            DemoCredential config = DemoCredentials.Get(consumerGroupName: "somesecondgroup");

            BlobContainerClient snapshotstorage = new(
                blobContainerUri: new($"https://{config.CheckpointStorage.StorageAccountName}.blob.core.windows.net/snapshots/"),
                credential: config.TokenCredential);

            using CancellationTokenSource cts = new();
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            EventHubObservableClient client = new(details: new EventHubConnectionDetails(
                credential: config.TokenCredential,
                eventHubNamespace: $"{config.EventHubInformation.EventHubNamespaceName}.servicebus.windows.net",
                eventHubName: config.EventHubInformation.EventHubInstanceName,
                consumerGroupName: config.EventHubInformation.ConsumerGroup,
                checkpointStorage: new(
                    blobContainerUri: new($"https://{config.CheckpointStorage.StorageAccountName}.blob.core.windows.net/{config.CheckpointStorage.StorageContainerName}/"), 
                    credential: config.TokenCredential)
                ));

            static Task<EventPosition> determinePosition(
                PartitionInitializingEventArgs partitionInitializingEventArgs,
                CancellationToken cancellationToken) => Task.FromResult(EventPosition.Earliest);

           var observable = client.CreateObservable(
               determinePosition: determinePosition,
               cancellationToken: cts.Token);

            static void HandleEvent(EventHubProcessorEvent e)
            {
                static string catchup(Event evnt)
                {
                    // Display for a received event how many other (newer) events wait in the same partition until the consumer has caught up...
                    TimeSpan ts = evnt.LastEnqueuedEventProperties.LastReceivedTime.Value.Subtract(evnt.EventData.EnqueuedTime);
                    long seq = evnt.LastEnqueuedEventProperties.SequenceNumber.Value - evnt.EventData.SequenceNumber;
                    return $"{ts} / {seq} events";
                };

                var message = e switch
                {
                    EventHubProcessorEvent.Event evnt => $"Event PartitionId={evnt.Item.PartitionContext.PartitionId} catchup=({catchup(evnt.Item)})",
                    EventHubProcessorEvent.Error error => $"Error: PartitionId={error.Item.PartitionId} Operation=\"{error.Item.Operation}\"",
                    EventHubProcessorEvent.PartitionInitializing pa => $"PartitionInitializing: PartitionId={pa.Item.PartitionId}",
                    EventHubProcessorEvent.PartitionClosing pc => $"PartitionClosing: PartitionId={pc.Item.PartitionId} reason={pc.Item.Reason}",
                    _ => throw new NotImplementedException(),
                };

                Console.Out.WriteLine(message);
            };

            using var sub = observable.Subscribe(
                onNext: HandleEvent,
                onError: (e) => { Console.Error.WriteLine(e.Message); });

            await Console.Out.WriteLineAsync("Press <return> to close");
            _ = await Console.In.ReadLineAsync();
            cts.Cancel();
        }
    }
}