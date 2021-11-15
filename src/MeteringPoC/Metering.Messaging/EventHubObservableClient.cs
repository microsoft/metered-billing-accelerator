namespace Metering.Messaging
{
    using Azure.Messaging.EventHubs;
    using Azure.Messaging.EventHubs.Consumer;
    using Azure.Messaging.EventHubs.Processor;
    using Metering.Types;
    using Metering.Types.EventHub;
    using Microsoft.FSharp.Core;
    using NodaTime;
    using System;
    using System.Reactive.Disposables;
    using System.Reactive.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public static class EventHubObservableClient
    {
        public static IObservable<EventHubProcessorEvent> CreateObservable(this EventProcessorClient processor, 
            Func<PartitionInitializingEventArgs, CancellationToken, Task<EventPosition>> determinePosition,
            CancellationToken cancellationToken = default)
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
                    o.OnNext(EventHubProcessorEvent.NewEventHubError(processErrorEventArgs));
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

        public static IObservable<(MeteringEvent, EventsToCatchup)> CreateAggregatorObservable(
            DemoCredential config, FSharpOption<MessagePosition> someMessagePosition, CancellationToken cancellationToken = default)
        {
            return Observable.Create<(MeteringEvent, EventsToCatchup)>(o =>
            {
                var eventHubConsumerClient = new EventHubConsumerClient(
                    consumerGroup: config.EventHubInformation.ConsumerGroup,
                    fullyQualifiedNamespace: $"{config.EventHubInformation.EventHubNamespaceName}.servicebus.windows.net",
                    eventHubName: config.EventHubInformation.EventHubInstanceName,
                    credential: config.TokenCredential);

                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var innerCancellationToken = cts.Token;

                _ = Task.Run(
                    async () =>
                    {
                        await foreach (var partitionEvent in eventHubConsumerClient.ReadEventsFromPartitionAsync(
                            partitionId: "",
                            startingPosition: MessagePositionModule.startingPosition(someMessagePosition),
                            readOptions: new ReadEventOptions() { TrackLastEnqueuedEventProperties = true },
                            cancellationToken: cts.Token))
                        {
                            try
                            {
                                var lastEnqueuedEvent = partitionEvent.Partition.ReadLastEnqueuedEventProperties();
                                var eventsToCatchup = new EventsToCatchup(
                                    numberOfEvents: lastEnqueuedEvent.SequenceNumber.Value - partitionEvent.Data.SequenceNumber,
                                    timeDelta: lastEnqueuedEvent.LastReceivedTime.Value.Subtract(partitionEvent.Data.EnqueuedTime));

                                var bodyString = partitionEvent.Data.EventBody.ToString();
                                var meteringUpdateEvent = Json.fromStr<MeteringUpdateEvent>(bodyString);
                                var meteringEvent = new MeteringEvent(
                                    meteringUpdateEvent: meteringUpdateEvent,
                                    messagePosition: new MessagePosition(
                                            partitionID: partitionEvent.Partition.ToString(),
                                            sequenceNumber: partitionEvent.Data.SequenceNumber,
                                            partitionTimestamp: ZonedDateTime.FromDateTimeOffset(partitionEvent.Data.EnqueuedTime)));

                                var item = (meteringEvent, eventsToCatchup);

                                o.OnNext(item);
                            }
                            catch (Exception ex)
                            {
                                await Console.Error.WriteLineAsync(ex.Message);
                            }
                            innerCancellationToken.ThrowIfCancellationRequested();
                        }

                        o.OnCompleted();
                    },
                    innerCancellationToken);

                return new CancellationDisposable(cts);
            });
        }
    }
}