namespace Metering.Messaging
{
    using Azure.Messaging.EventHubs;
    using Azure.Messaging.EventHubs.Consumer;
    using Azure.Messaging.EventHubs.Processor;
    using System;
    using System.Reactive.Disposables;
    using System.Reactive.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Types;

    public class EventHubObservableClient
    {
        private readonly EventHubConnectionDetails EventHubConnectionDetails;

        private readonly EventProcessorClient processor;

        public EventHubObservableClient(EventHubConnectionDetails details)
        {
            this.EventHubConnectionDetails = details;

            this.processor = new(
                 checkpointStore: details.CheckpointStorage,
                 consumerGroup: details.ConsumerGroupName,
                 fullyQualifiedNamespace: details.EventHubNamespace,
                 eventHubName: details.EventHubName,
                 credential: details.Credential,
                 clientOptions: new()
                 {
                     TrackLastEnqueuedEventProperties = true,
                     PrefetchCount = 100,
                 });
        }

        public IObservable<EventHubProcessorEvent> CreateObservable(
            Func<PartitionInitializingEventArgs, CancellationToken, Task<EventPosition>> determinePosition,
            CancellationToken cancellationToken)
        {
            return Observable.Create<EventHubProcessorEvent>(o =>
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var innerCancellationToken = cts.Token;

                async Task ProcessEvent(ProcessEventArgs processEventArgs)
                {
                    o.OnNext(EventHubProcessorEvent.NewEvent(new Event(
                        eventData: processEventArgs.Data, 
                        lastEnqueuedEventProperties: processEventArgs.Partition.ReadLastEnqueuedEventProperties(), 
                        partitionContext: processEventArgs.Partition)));
                    await processEventArgs.UpdateCheckpointAsync(processEventArgs.CancellationToken);

                };
                Task ProcessError (ProcessErrorEventArgs processErrorEventArgs)
                {
                    o.OnNext(EventHubProcessorEvent.NewError(processErrorEventArgs));
                    return Task.CompletedTask;
                };
                async Task PartitionInitializing(PartitionInitializingEventArgs partitionInitializingEventArgs)
                {
                    partitionInitializingEventArgs.DefaultStartingPosition = await determinePosition(partitionInitializingEventArgs, innerCancellationToken);
                    o.OnNext(EventHubProcessorEvent.NewPartitionInitializing(partitionInitializingEventArgs));
                };
                Task PartitionClosing(PartitionClosingEventArgs partitionClosingEventArgs)
                {
                    o.OnNext(EventHubProcessorEvent.NewPartitionClosing(partitionClosingEventArgs));
                    return Task.CompletedTask;
                };

                _ = Task.Run(async () => {
                    try
                    {
                        processor.ProcessEventAsync += ProcessEvent;
                        processor.ProcessErrorAsync += ProcessError;
                        processor.PartitionInitializingAsync += PartitionInitializing;
                        processor.PartitionClosingAsync += PartitionClosing;
                        
                        await processor.StartProcessingAsync(cancellationToken: innerCancellationToken);
                        
                        // This will block until the cancellationToken gets pulled
                        await Task.Delay(
                            millisecondsDelay: Timeout.Infinite,
                            cancellationToken: innerCancellationToken);
                        
                        o.OnCompleted();
                        await processor.StopProcessingAsync();
                    }
                    catch (TaskCanceledException) { }
                    catch (Exception e) { 
                        o.OnError(e); 
                    }
                    finally
                    {
                        processor.ProcessEventAsync -= ProcessEvent;
                        processor.ProcessErrorAsync -= ProcessError;
                        processor.PartitionInitializingAsync -= PartitionInitializing;
                        processor.PartitionClosingAsync -= PartitionClosing;
                    }
                }, cancellationToken: innerCancellationToken);

                return new CancellationDisposable(cts);
            });
        }
    }
}