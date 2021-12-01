// See https://aka.ms/new-console-template for more information

using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Processor;
using Metering.Types;
using Metering.Types.EventHub;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using SomeMeterCollection = Microsoft.FSharp.Core.FSharpOption<Metering.Types.MeterCollection>;

Console.WriteLine("Dump Hub");

static IObservable<EventHubProcessorEvent<TState, TEvent>> CreateObservable<TState, TEvent>(
    EventProcessorClient processor,
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

        Task ProcessError(ProcessErrorEventArgs processErrorEventArgs)
        {
            o.OnNext(EventHubProcessorEvent<TState, TEvent>.NewEventHubError(
                new Tuple<PartitionID, Exception>(
                    PartitionID.NewPartitionID(processErrorEventArgs.PartitionId),
                    processErrorEventArgs.Exception)));
            return Task.CompletedTask;
        };

        Task PartitionInitializing(PartitionInitializingEventArgs partitionInitializingEventArgs)
        {
            Console.WriteLine($"Initializing {partitionInitializingEventArgs.PartitionId}");

            partitionInitializingEventArgs.DefaultStartingPosition = EventPosition.Earliest;

            var evnt = EventHubProcessorEvent<TState, TEvent>.NewPartitionInitializing(
                new PartitionInitializing<TState>(
                    partitionInitializingEventArgs: partitionInitializingEventArgs,
                    initialState: default));
            o.OnNext(evnt);
            return Task.CompletedTask;
        };

        Task PartitionClosing(PartitionClosingEventArgs partitionClosingEventArgs)
        {
            Console.WriteLine($"Closing {partitionClosingEventArgs.PartitionId}");

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
            catch (Exception e)
            {
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

// https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Azure.Messaging.EventHubs/samples/Sample05_ReadingEvents.md

Console.Title = Assembly.GetExecutingAssembly().GetName().Name;

var config = MeteringConnectionsModule.getFromEnvironment();

var meteringConfig = MeteringConfigurationProviderModule.create(config: config,
    marketplaceClient: MarketplaceClient.submitCsharp.ToFSharpFunc());

Console.WriteLine($"Reading from {config.EventProcessorClient.FullyQualifiedNamespace}");

using CancellationTokenSource cts = new();

CreateObservable<SomeMeterCollection, MeteringUpdateEvent>(config.EventProcessorClient,
        converter: x => Json.fromStr<MeteringUpdateEvent>(x.EventBody.ToString()),
        cancellationToken: cts.Token)
    .Subscribe(onNext: e => {
        if (e.IsEventHubEvent)
    {
            var ev = EventHubProcessorEvent.getEvent<SomeMeterCollection, MeteringUpdateEvent>(e);

            //Console.WriteLine(EventHubProcessorEvent.toStr(FSharpFunc<MeteringUpdateEvent, string>.FromConverter(MeteringUpdateEventModule.toStr), e));
            Console.WriteLine(ev);
    }
});

await Console.Out.WriteLineAsync("Press <Return> to close...");
_ = await Console.In.ReadLineAsync();
cts.Cancel();
