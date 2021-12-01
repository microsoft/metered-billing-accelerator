namespace Metering

open System
open System.Threading
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Reactive.Disposables
open System.Reactive.Linq
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Processor
open Azure.Messaging.EventHubs.Consumer
open Metering.Types
open Metering.Types.EventHub

[<Extension>]
module EventHubObservableClient =
    [<Extension>]
    let CreateEventHubProcessorEventObservableFSharp<'TState, 'TEvent>
        (processor: EventProcessorClient)
        (determineInitialState: PartitionInitializingEventArgs -> CancellationToken -> Task<'TState>)
        (determinePosition: 'TState -> EventPosition)
        (converter: EventData -> 'TEvent)
        ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken)
        : IObservable<EventHubProcessorEvent<'TState, 'TEvent>> =

        let fsharpFunction (o: IObserver<EventHubProcessorEvent<'TState, 'TEvent>>) : IDisposable =
            let cts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)

            let innerCancellationToken = cts.Token

            let ProcessEvent (processEventArgs: ProcessEventArgs) =
                match (EventHubEvent.create processEventArgs converter) with
                | Some e -> 
                    
                    Console.ForegroundColor <- ConsoleColor.Red; printfn "\n\n%A\n\n" e; Console.ResetColor()

                    o.OnNext(EventHubEvent e)
                | None -> ()

                // We're not doing checkpointing here, but let that happen downsteam... That's why EventHubProcessorEvent contains the ProcessEventArgs
                // processEventArgs.UpdateCheckpointAsync(processEventArgs.CancellationToken);
                Task.CompletedTask

            let ProcessError (processErrorEventArgs: ProcessErrorEventArgs) =
                let partitionId =
                    processErrorEventArgs.PartitionId |> PartitionID

                let ex = processErrorEventArgs.Exception
                //let evnt = EventHubError(partitionId, ex)
                o.OnError(ex)
                Task.CompletedTask

            let PartitionClosing (partitionClosingEventArgs: PartitionClosingEventArgs) =
                let evnt: EventHubProcessorEvent<'TState, 'TEvent> =
                    PartitionClosing { PartitionClosingEventArgs = partitionClosingEventArgs }

                o.OnCompleted()
                Task.CompletedTask

            let PartitionInitializing (partitionInitializingEventArgs: PartitionInitializingEventArgs) : Task =
                task {
                    let! (initialState: 'TState) =
                        determineInitialState partitionInitializingEventArgs innerCancellationToken

                    let startingPosition = determinePosition initialState
                    partitionInitializingEventArgs.DefaultStartingPosition <- startingPosition

                    let evnt =
                        PartitionInitializing
                            { PartitionInitializingEventArgs = partitionInitializingEventArgs
                              InitialState = initialState }

                    o.OnNext(evnt)
                    return ()
                }

            let createTask () : Task =
                let a =
                    async {
                        try
                            processor.add_ProcessEventAsync ProcessEvent
                            processor.add_ProcessErrorAsync ProcessError
                            processor.add_PartitionInitializingAsync PartitionInitializing
                            processor.add_PartitionClosingAsync PartitionClosing

                            try
                                let! () =
                                    processor.StartProcessingAsync(innerCancellationToken)
                                    |> Async.AwaitTask

                                // This will block until the cancellationToken gets pulled
                                let! () =
                                    Task.Delay(Timeout.Infinite, innerCancellationToken)
                                    |> Async.AwaitTask

                                o.OnCompleted()
                                let! () = processor.StopProcessingAsync() |> Async.AwaitTask

                                return ()
                            with
                            | :? TaskCanceledException -> ()
                            | e -> o.OnError(e)
                        finally
                            processor.remove_ProcessEventAsync ProcessEvent
                            processor.remove_ProcessErrorAsync ProcessError
                            processor.remove_PartitionInitializingAsync PartitionInitializing
                            processor.remove_PartitionClosingAsync PartitionClosing
                    }

                Async.StartAsTask(a, cancellationToken = innerCancellationToken)


            let _ =
                Task.Run(
                    ``function`` = (((fun () -> createTask ()): (unit -> Task)): Func<Task>),
                    cancellationToken = innerCancellationToken
                )

            new CancellationDisposable(cts) :> IDisposable

        Observable.Create<EventHubProcessorEvent<'TState, 'TEvent>>(fsharpFunction)

    let create (config: MeteringConfigurationProvider) (cancellationToken: CancellationToken) = 
        
        let determineInitialState (args: PartitionInitializingEventArgs) ct =
            MeterCollectionStore.loadLastState 
                config 
                (args.PartitionId |> PartitionID.create)
                ct

        CreateEventHubProcessorEventObservableFSharp
            config.MeteringConnections.EventProcessorClient
            determineInitialState
            MeterCollection.getEventPosition
            (fun x -> Json.fromStr<MeteringUpdateEvent>(x.EventBody.ToString()))
            cancellationToken
        |> (fun x -> Observable.GroupBy(x, EventHubProcessorEvent.partitionId))

