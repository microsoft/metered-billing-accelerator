namespace Metering.Messaging
{
    using Azure.Messaging.EventHubs;
    using Azure.Messaging.EventHubs.Consumer;
    using Azure.Messaging.EventHubs.Processor;
    using Metering.Types;
    using Metering.Types.EventHub;
    using Microsoft.FSharp.Core;
    using System;
    using System.Reactive.Disposables;
    using System.Reactive.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public static class EventHubObservableClient
    {
        public static IObservable<EventHubProcessorEvent<TState, TEvent>> CreateEventHubProcessorEventObservable<TState, TEvent>(
            this EventProcessorClient processor,
            Func<PartitionInitializingEventArgs, CancellationToken, Task<TState>> determineInitialState,
            Func<TState, EventPosition> determinePosition,
            Func<EventData, TEvent> converter,
            CancellationToken cancellationToken = default)
        {
            return Observable.Create<EventHubProcessorEvent<TState, TEvent>>(o =>
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var innerCancellationToken = cts.Token;

                Task ProcessEvent(ProcessEventArgs processEventArgs)
                {
                    var e = EventHubEvent.create(processEventArgs, converter.ToFSharpFunc());
                    if (e.IsSome())
                    {
                        o.OnNext(EventHubProcessorEvent<TState, TEvent>.NewEventHubEvent(e.Value));
                    }

                    // We're not doing checkpointing here, but let that happen downsteam... That's why EventHubProcessorEvent contains the ProcessEventArgs
                    // processEventArgs.UpdateCheckpointAsync(processEventArgs.CancellationToken);
                    return Task.CompletedTask; 
                };

                Task ProcessError (ProcessErrorEventArgs processErrorEventArgs)
                {
                    o.OnNext(EventHubProcessorEvent<TState, TEvent>.NewEventHubError(
                       new Tuple<PartitionID, Exception>(
                           PartitionID.NewPartitionID(processErrorEventArgs.PartitionId),
                           processErrorEventArgs.Exception)));

                    return Task.CompletedTask;
                };

                async Task PartitionInitializing(PartitionInitializingEventArgs partitionInitializingEventArgs)
                {
                    var initialState = await determineInitialState(partitionInitializingEventArgs, innerCancellationToken);
                    partitionInitializingEventArgs.DefaultStartingPosition = determinePosition(initialState); 
                    

                    var evnt = EventHubProcessorEvent<TState, TEvent>.NewPartitionInitializing(
                        new PartitionInitializing<TState>(
                            partitionInitializingEventArgs: partitionInitializingEventArgs,
                            initialState: initialState));
                    o.OnNext(evnt);
                };

                Task PartitionClosing(PartitionClosingEventArgs partitionClosingEventArgs)
                {
                    var evnt = EventHubProcessorEvent<TState, TEvent>.NewPartitionClosing(
                        new PartitionClosing(
                            partitionClosingEventArgs));
                    o.OnNext(evnt);
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