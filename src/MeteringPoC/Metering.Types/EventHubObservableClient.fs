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
open System.Text

[<Extension>]
module EventHubObservableClient =
    let private createInternal<'TState, 'TEvent>
        (processor: EventProcessorClient)
        (determineInitialState: PartitionInitializingEventArgs -> CancellationToken -> Task<'TState>)
        (determinePosition: 'TState -> EventPosition)
        (converter: EventData -> 'TEvent)
        ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken)
        : IObservable<EventHubProcessorEvent<'TState, 'TEvent>> =

        let a = Action (fun () -> eprintf "outside cancelled")
        cancellationToken.Register(a) |> ignore

        let fsharpFunction (o: IObserver<EventHubProcessorEvent<'TState, 'TEvent>>) : IDisposable =
            let cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)

            let innerCancellationToken = cts.Token

            let x = Action (fun () -> 
                eprintfn "innerCancellationToken is Cancelled"
            )                        
            innerCancellationToken.Register(callback = x) |> ignore

            let ProcessEvent (processEventArgs: ProcessEventArgs) =
                try
                    match (EventHubEvent.createFromEventHub converter processEventArgs) with
                    | Some e ->                     
                        // Console.ForegroundColor <- ConsoleColor.Red; printfn "\n\n%A\n\n" e; Console.ResetColor()
                        // printfn $"\n\nnew event {e.MessagePosition.PartitionID |> PartitionID.value}-{e.MessagePosition.SequenceNumber} {e}" 
                        o.OnNext(EventHubEvent e)
                    | None -> ()
                with
                | e -> eprintf $"ProcessEvent Exception {e.Message} " ;()

                // We're not doing checkpointing here, but let that happen downsteam... That's why EventHubProcessorEvent contains the ProcessEventArgs
                // processEventArgs.UpdateCheckpointAsync(processEventArgs.CancellationToken);
                Task.CompletedTask

            let ProcessError (processErrorEventArgs: ProcessErrorEventArgs) =
                try
                    let partitionId =
                        processErrorEventArgs.PartitionId |> PartitionIdentifier.createId

                    let ex = processErrorEventArgs.Exception
                    o.OnError(ex)
                with
                | e -> eprintf $"ProcessError Exception {e.Message}" ;()
                
                Task.CompletedTask

            let PartitionClosing (partitionClosingEventArgs: PartitionClosingEventArgs) =
                try
                    let evnt: EventHubProcessorEvent<'TState, 'TEvent> =
                        PartitionClosing { PartitionClosingEventArgs = partitionClosingEventArgs }

                    o.OnCompleted()
                with
                | e -> eprintf $"PartitionClosing Exception {e.Message}" ;()

                Task.CompletedTask

            let PartitionInitializing (partitionInitializingEventArgs: PartitionInitializingEventArgs) : Task =
                task {
                    try
                        let! (initialState: 'TState) =
                            determineInitialState partitionInitializingEventArgs innerCancellationToken

                        printfn "Initial state %A" initialState
                        let startingPosition = determinePosition initialState
                        partitionInitializingEventArgs.DefaultStartingPosition <- startingPosition

                        printfn "Starting position %A" startingPosition

                        let evnt =
                            PartitionInitializing
                                { PartitionInitializingEventArgs = partitionInitializingEventArgs
                                  InitialState = initialState }

                        o.OnNext(evnt)
                    with
                    | e -> eprintf $"PartitionInitializing Exception {e.Message}"

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

    [<Extension>]
    let toMeteringUpdateEvent (eventData: EventData) : MeteringUpdateEvent =
        // eventData.Body.ToArray() |> Encoding.UTF8.GetString |> Json.fromStr<MeteringUpdateEvent>
        let s = eventData.EventBody.ToString()

        try 
            match s |> Json.fromStr2<MeteringUpdateEvent> with
            | Ok v -> v
            | Error _ -> 
                eventData.EventBody.ToArray()
                |> UnprocessableByteContent
                |> UnprocessableMessage
        with
        | _ -> s |> UnprocessableStringContent |> UnprocessableMessage

    [<Extension>]
    let create (config: MeteringConfigurationProvider) (cancellationToken: CancellationToken) = 
        
        let determineInitialState (args: PartitionInitializingEventArgs) ct =
            MeterCollectionStore.loadLastState 
                config 
                (args.PartitionId |> PartitionID.create)
                ct

        createInternal
            (config.MeteringConnections |> MeteringConnections.createEventProcessorClient)
            determineInitialState
            MeterCollectionLogic.getEventPosition
            toMeteringUpdateEvent
            cancellationToken
        |> (fun x -> Observable.GroupBy(x, EventHubProcessorEvent.partitionId))
