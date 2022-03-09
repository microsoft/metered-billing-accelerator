// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Integration

open System
open System.Threading
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Reactive.Disposables
open System.Reactive.Linq
open Microsoft.Extensions.Logging
open Azure.Messaging.EventHubs
open Azure.Messaging.EventHubs.Processor
open Azure.Messaging.EventHubs.Consumer
open Metering.BaseTypes
open Metering.BaseTypes.EventHub
open Metering.EventHub


[<Extension>]
module EventHubObservableClient =
    type internal EventHubCaptureConf =
        | CanReadEverythingFromEventHub of EventPosition
        | ReadFromEventHubCaptureAndThenEventHub of LastProcessedSequenceNumber:SequenceNumber * LastProcessedEventTimestamp:MeteringDateTime
        | ReadFromEventHubCaptureBeginningAndThenEventHub

    let private createInternal<'TState, 'TEvent>
        (logger: ILogger)
        (config: MeteringConfigurationProvider) 
        (determineInitialState: PartitionInitializingEventArgs -> CancellationToken -> Task<'TState>)
        (determinePosition: 'TState -> StartingPosition)
        (converter: EventData -> 'TEvent)
        ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken)
        : IObservable<EventHubProcessorEvent<'TState, 'TEvent>> =

        
        let registerCancellationMessage (message: string) (ct: CancellationToken)  =            
            Action (fun () -> logger.LogWarning(sprintf "%s" message))
            |> ct.Register
            |> ignore

        cancellationToken |> registerCancellationMessage "outer cancellationToken pulled"

        let fsharpFunction (o: IObserver<EventHubProcessorEvent<'TState, 'TEvent>>) : IDisposable =
            let cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)

            let cancellationToken = cts.Token
            cancellationToken |> registerCancellationMessage "innerCancellationToken is Cancelled"

            let ProcessEvent (processEventArgs: ProcessEventArgs) : Task =
                try
                    match (EventHubIntegration.createEventHubEventFromEventData converter processEventArgs) with
                    | Some e -> o.OnNext(EventHubEvent e)
                    | None -> 
                        let catchUp = processEventArgs.Partition.ReadLastEnqueuedEventProperties()
                        logger.LogDebug <| sprintf $"Didn't find events: PartitionId {processEventArgs.Partition.PartitionId} SequenceNumber {catchUp.SequenceNumber} EnqueuedTime {catchUp.EnqueuedTime} LastReceivedTime {catchUp.LastReceivedTime} ###############"

                        ()
                with
                | e -> 
                    eprintf $"ProcessEvent Exception {e.Message} "
                    ()

                // We're not doing checkpointing here, but let that happen downsteam... That's why EventHubProcessorEvent contains the ProcessEventArgs
                // processEventArgs.UpdateCheckpointAsync(processEventArgs.CancellationToken);
                Task.CompletedTask

            let ProcessError (processErrorEventArgs: ProcessErrorEventArgs) : Task =
                try
                    o.OnError(processErrorEventArgs.Exception)
                with
                | e -> logger.LogCritical <| sprintf $"ProcessError Exception {e.Message}" ;()
                
                Task.CompletedTask

            let PartitionClosing (partitionClosingEventArgs: PartitionClosingEventArgs) : Task =
                try
                    if partitionClosingEventArgs.CancellationToken.IsCancellationRequested
                    then eprintfn "PartitionClosing partitionClosingEventArgs.CancellationToken.IsCancellationRequested"
                    
                    match partitionClosingEventArgs.Reason with
                    | ProcessingStoppedReason.OwnershipLost -> eprintfn $"{partitionClosingEventArgs.PartitionId}: ProcessingStoppedReason.OwnershipLost"
                    | ProcessingStoppedReason.Shutdown  -> eprintfn $"{partitionClosingEventArgs.PartitionId}: ProcessingStoppedReason.Shutdown"
                    | a -> logger.LogCritical <| $"{partitionClosingEventArgs.PartitionId}: ProcessingStoppedReason {a}"

                    o.OnCompleted()
                with
                | e -> logger.LogCritical <| $"PartitionClosing Exception {e.Message}" ;()

                Task.CompletedTask

            let PartitionInitializing (partitionInitializingEventArgs: PartitionInitializingEventArgs) : Task =
                task {
                    try
                        let partitionIdStr = partitionInitializingEventArgs.PartitionId

                        let! (initialState: 'TState) =
                            determineInitialState partitionInitializingEventArgs cancellationToken

                        let initialPosition = determinePosition initialState

                        let eventHubStartPosition =
                            match initialPosition with
                            | StartingPosition.Earliest -> 
                                ReadFromEventHubCaptureBeginningAndThenEventHub                            
                            | StartingPosition.NextEventAfter (lastProcessedEventSequenceNumber, lastProcessedEventTimestamp) ->  
                                // Let's briefly check if the desired event is still avail in EventHub,
                                // otherwise we need to crawl through EventHub Capture
                                let consumerClient = 
                                    config.MeteringConnections
                                    |> MeteringConnections.createEventHubConsumerClient
                                    
                                let partitionProps = consumerClient.GetPartitionPropertiesAsync(
                                    partitionId = partitionIdStr, 
                                    cancellationToken = cancellationToken).Result

                                let desiredEventIsNotAvailableInEventHub =
                                    let desiredEvent = lastProcessedEventSequenceNumber + 1L
                                    let firstOneWeCanRead = partitionProps.BeginningSequenceNumber
                                    desiredEvent < firstOneWeCanRead

                                if desiredEventIsNotAvailableInEventHub
                                then 
                                    ReadFromEventHubCaptureAndThenEventHub(
                                        LastProcessedSequenceNumber = lastProcessedEventSequenceNumber, 
                                        LastProcessedEventTimestamp = lastProcessedEventTimestamp)
                                else 
                                    // If isInclusive=true, the specified event (nextEventAfter) is included; otherwise the next event is returned.
                                    // We *cannot* do 
                                    //     EventPosition.FromSequenceNumber(nextEventAfter + 1L, isInclusive = true)
                                    // , as that crashes if nextEventAfter is the last one
                                    CanReadEverythingFromEventHub (
                                        EventPosition.FromSequenceNumber(
                                            sequenceNumber = lastProcessedEventSequenceNumber, 
                                            isInclusive = false))

                        match eventHubStartPosition with
                        | CanReadEverythingFromEventHub eventHubStartPosition -> 
                            o.OnNext (PartitionInitializing
                            { PartitionID = partitionIdStr |> PartitionID.create
                              InitialState = initialState })

                            partitionInitializingEventArgs.DefaultStartingPosition <- eventHubStartPosition
                        | ReadFromEventHubCaptureBeginningAndThenEventHub -> 
                            o.OnNext (PartitionInitializing
                                { PartitionID = partitionIdStr |> PartitionID.create
                                  InitialState = initialState })
                            
                            let lastProcessedEventReadFromCaptureSequenceNumber = 
                                CaptureProcessor.readAllEvents
                                    converter
                                    (partitionInitializingEventArgs.PartitionId |> PartitionID.create)
                                    cancellationToken
                                    config.MeteringConnections
                                |> Seq.map (fun e -> 
                                    o.OnNext(EventHubEvent e)
                                    e.MessagePosition.SequenceNumber
                                )
                                |> Seq.tryLast

                            match lastProcessedEventReadFromCaptureSequenceNumber with
                            | None -> 
                                partitionInitializingEventArgs.DefaultStartingPosition <- 
                                    EventPosition.Earliest
                            | Some sequenceNumber ->
                                partitionInitializingEventArgs.DefaultStartingPosition <- 
                                    EventPosition.FromSequenceNumber(
                                        sequenceNumber = sequenceNumber, 
                                        isInclusive = false)                            

                                
                        | ReadFromEventHubCaptureAndThenEventHub(LastProcessedSequenceNumber = sn; LastProcessedEventTimestamp = t) ->
                            o.OnNext (PartitionInitializing
                                { PartitionID = partitionIdStr |> PartitionID.create
                                  InitialState = initialState })
                             
                            let lastProcessedEventReadFromCaptureSequenceNumber = 
                                CaptureProcessor.readEventsFromPosition
                                    converter
                                    (MessagePosition.createData partitionIdStr sn t)
                                    cancellationToken
                                    config.MeteringConnections
                                |> Seq.map (fun e -> 
                                    o.OnNext(EventHubEvent e)
                                    e.MessagePosition.SequenceNumber
                                )
                                |> Seq.tryLast
                                
                            match lastProcessedEventReadFromCaptureSequenceNumber with
                            | None -> 
                                partitionInitializingEventArgs.DefaultStartingPosition <- 
                                    EventPosition.Earliest
                            | Some sequenceNumber ->
                                partitionInitializingEventArgs.DefaultStartingPosition <- 
                                    EventPosition.FromSequenceNumber(
                                        sequenceNumber = sequenceNumber, 
                                        isInclusive = false)
                    with
                    | e -> logger.LogCritical <| $"PartitionInitializing Exception {e.Message}"

                    return ()
                }

            let createTask () : Task =
                let a =
                    async {
                        let processor = config.MeteringConnections |> MeteringConnections.createEventProcessorClient
                        try                            
                            processor.add_ProcessEventAsync ProcessEvent
                            processor.add_ProcessErrorAsync ProcessError
                            processor.add_PartitionInitializingAsync PartitionInitializing
                            processor.add_PartitionClosingAsync PartitionClosing

                            try
                                let! () =
                                    processor.StartProcessingAsync(cancellationToken)
                                    |> Async.AwaitTask

                                // This will block until the cancellationToken gets pulled
                                let! () =
                                    Task.Delay(Timeout.Infinite, cancellationToken)
                                    |> Async.AwaitTask

                                o.OnCompleted()
                                let! () = processor.StopProcessingAsync() |> Async.AwaitTask

                                return ()
                            with
                            | :? TaskCanceledException -> ()
                            | e -> o.OnError(e)
                        finally
                            try
                                processor.remove_ProcessEventAsync ProcessEvent
                            with 
                                e -> ()

                            try
                                processor.remove_ProcessErrorAsync ProcessError
                            with 
                                e -> ()

                            try
                                processor.remove_PartitionInitializingAsync PartitionInitializing
                            with
                                e -> ()

                            try
                                processor.remove_PartitionClosingAsync PartitionClosing
                            with
                                e -> ()
                            
                    }

                Async.StartAsTask(a, cancellationToken = cancellationToken)

            let _ =
                Task.Run(
                    ``function`` = (((fun () -> createTask ()): (unit -> Task)): Func<Task>),
                    cancellationToken = cancellationToken
                )

            new CancellationDisposable(cts) :> IDisposable

        Observable.Create<EventHubProcessorEvent<'TState, 'TEvent>>(fsharpFunction)
        
    [<Extension>]
    let create (logger: ILogger) (config: MeteringConfigurationProvider) (cancellationToken: CancellationToken) = 
        let determineInitialState (args: PartitionInitializingEventArgs) ct =
            let pid = args.PartitionId |> PartitionID.create
            MeterCollectionStore.loadLastState config pid ct

        createInternal logger config determineInitialState MeterCollectionLogic.getEventPosition CaptureProcessor.toMeteringUpdateEvent cancellationToken
        |> (fun x -> Observable.GroupBy(x, EventHubProcessorEvent.partitionId))

