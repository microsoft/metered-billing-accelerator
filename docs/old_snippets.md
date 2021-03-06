
# Some old code snippets I want to keep around

## Download the EventHub capture

```bash
az login --service-principal --username %AZURE_METERING_INFRA_CLIENT_ID% --password %AZURE_METERING_INFRA_CLIENT_SECRET% --tenant %AZURE_METERING_INFRA_TENANT_ID%

az storage blob download-batch --destination . --source %AZURE_METERING_INFRA_CAPTURE_CONTAINER%
```

## EventHubObservableClient on C#

```csharp
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
            
            return Observable.Create<EventHubProcessorEvent<TState, TEvent>>(subscribe: o =>
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
                           PartitionID.NewPartitionID(processErrorEventArgs.PartitionId),
                           processErrorEventArgs.Exception));

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
```


## Calling that client


```csharp
    Task<SomeMeterCollection> determineInitialState (PartitionInitializingEventArgs arg, CancellationToken ct) => 
        MeterCollectionStore.loadLastState(
            snapshotContainerClient: connections.SnapshotStorage,
            partitionID: PartitionIDModule.create(arg.PartitionId),
            cancellationToken: ct);
    IObservable<IGroupedObservable<PartitionID, EventHubProcessorEvent<SomeMeterCollection, MeteringUpdateEvent>>> groupedByPartitionId =
        connections.EventProcessorClient
            .CreateEventHubProcessorEventObservable<SomeMeterCollection, MeteringUpdateEvent>(
                determineInitialState: determineInitialState,
                determinePosition: MeterCollectionModule.getEventPosition,
                converter: x => Json.fromStr<MeteringUpdateEvent>(x.EventBody.ToString()),
                cancellationToken: cts.Token)
            .GroupBy(EventHubProcessorEvent.partitionId);
 
    // Calling the F# extension from C#
    IObservable<IGroupedObservable<PartitionID, EventHubProcessorEvent<SomeMeterCollection, MeteringUpdateEvent>>> groupedByPartitionId =
        config.EventProcessorClient
            .CreateEventHubProcessorEventObservableFSharp<SomeMeterCollection, MeteringUpdateEvent>(
                determineInitialState: FSharpFuncUtil.ToFSharpFunc<PartitionInitializingEventArgs, CancellationToken, Task<SomeMeterCollection>>(determineInitialState),
                determinePosition: FSharpFuncUtil.ToFSharpFunc<SomeMeterCollection, EventPosition>(func: MeterCollectionModule.getEventPosition),
                converter: FSharpFuncUtil.ToFSharpFunc<EventData, MeteringUpdateEvent>(x => Json.fromStr<MeteringUpdateEvent>(x.EventBody.ToString())),
                cancellationToken: cts.Token)
            .GroupBy(EventHubProcessorEvent.partitionId);            
```

## Non-working biz logic

```csharp
namespace Metering
{
    using Azure.Messaging.EventHubs.Consumer;
    using Azure.Messaging.EventHubs.Processor;
    using Azure.Storage.Blobs;
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Messaging;
    using Metering.Types.EventHub;
    using Metering.Types;
    using Microsoft.FSharp.Core;
    using Azure;
    using Newtonsoft.Json;
    using System.IO;
    using System.IO.Compression;
    using System.Text;
    using System.Collections.Generic;
    using System.Linq;

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

    public interface IMessagePositionMessageClient<TMessagePayload>
    {
        IObservable<InternalUsageEvent> CreateMessagePositionObervable(SeekPosition startingPosition, CancellationToken cancellationToken = default);
    }

    class MeteringProcessor
    {
        private readonly Func<MeteringState, MeteringUpdateEvent, MeteringState> applyUpdate;
        private readonly Func<MeteringState> createEmb;
        private readonly BlobContainerClient snapshotContainerClient;
        private CancellationTokenSource cts;
        public MeteringState MeteringState { get; private set; }

        public MeteringProcessor(
           // IDistributedSearchConfiguration demoCredential,
           Func<MeteringState> createEmptyBusinessData,
           Func<MeteringState, MeteringUpdateEvent, MeteringState> applyUpdate,
           BlobContainerClient snapshotContainerClient)
        {
            this.applyUpdate = applyUpdate;
            this.createEmptyBusinessData = createEmptyBusinessData;
            this.snapshotContainerClient = snapshotContainerClient;
        }

        public async Task StartUpdateProcess(CancellationToken cancellationToken = default)
        {
            IObservable<MeteringState> businessDataObservable = await this.CreateObservable(cancellationToken);
            businessDataObservable.Subscribe(
                onNext: bd => this.MeteringState = bd,
                cancellationToken);
        }

        public async Task<IObservable<MeteringState>> CreateObservable(CancellationToken cancellationToken = default)
        {
            this.cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var updateFunc = this.applyUpdate.ToFSharpFunc();

            FSharpResult<MeteringState, BusinessDataUpdateError> snapshotResult = await this.FetchBusinessDataSnapshot(this.cts.Token);
            if (snapshotResult.IsError)
            {
                return Observable.Throw<MeteringState>(((BusinessDataUpdateError.SnapshotDownloadError)snapshotResult.ErrorValue).Item);
            }

            var snapshot = snapshotResult.ResultValue;

            IConnectableObservable<MeteringState> connectableObservable =
                this.updateMessagingClient
                    .CreateMessagePositionObervable(
                        startingPosition: SeekPosition.NewFromMessagePosition(
                            snapshot.MessagePosition.Add(1)),
                        cancellationToken: this.cts.Token)
                    .Scan(
                        seed: snapshotResult,
                        accumulator: (businessData, updateMessage)
                            => updateBusinessData(updateFunc, businessData, updateMessage)) // .Where(d => d.IsOk)
                    .Select(d => d.ResultValue)
                    .StartWith(snapshot)
                    .Publish(initialValue: snapshot);

            _ = connectableObservable.Connect();

            return connectableObservable.AsObservable();
        }

        public async Task<FSharpResult<MeteringState, BusinessDataUpdateError>> FetchBusinessDataSnapshot(CancellationToken cancellationToken)
        {
            try
            {
                FSharpOption<(MessagePosition, string)> someMessagePositionAndName = await this.GetLatestSnapshotID(cancellationToken);

                if (FSharpOption<(MessagePosition, string)>.get_IsNone(someMessagePositionAndName))
                {
                    var emptyBusinessData = new MeteringState(
                            data: this.createEmptyBusinessData(),
                            MessagePosition: MessagePosition.NewMessagePosition(-1));

                    return FSharpResult<MeteringState, BusinessDataUpdateError>.NewOk(emptyBusinessData);
                }

                var (MessagePosition, blobName) = someMessagePositionAndName.Value;
                await Console.Out.WriteLineAsync($"Loading snapshot MessagePosition {MessagePosition.Item} from {blobName}");

                var blobClient = this.snapshotContainerClient.GetBlobClient(blobName: blobName);
                var result = await blobClient.DownloadAsync(cancellationToken: cancellationToken);
                var val = await result.Value.Content.ReadJSON<MeteringState>();

                return FSharpResult<MeteringState, BusinessDataUpdateError>.NewOk(val);
            }
            catch (Exception ex)
            {
                return FSharpResult<MeteringState, BusinessDataUpdateError>.NewError(
                    BusinessDataUpdateError.NewSnapshotDownloadError(ex));
            }
        }

        public async Task<string> WriteBusinessDataSnapshot(MeteringState businessData, CancellationToken cancellationToken = default)
        {
            var blobName = MessagePositionToBlobName(businessData);
            var blobClient = this.snapshotContainerClient.GetBlobClient(blobName: blobName);

            try
            {
                _ = await blobClient.UploadAsync(content: businessData.AsJSONStream(), overwrite: false, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException rfe) when (rfe.ErrorCode == "BlobAlreadyExists")
            {
            }

            return blobClient.Name;
        }

        public async Task<IEnumerable<(MessagePosition, DateTimeOffset)>> GetOldSnapshots(TimeSpan maxAge, CancellationToken cancellationToken)
        {
            var blobs = this.snapshotContainerClient.GetBlobsAsync(traits: BlobTraits.Metadata, cancellationToken: cancellationToken);
            var items = new List<(MessagePosition, DateTimeOffset)>();
            await foreach (var blob in blobs)
            {
                var someUpdateMessagePosition = ParseMessagePositionFromBlobName(blob.Name);

                if (FSharpOption<MessagePosition>.get_IsSome(someUpdateMessagePosition) &&
                    blob.Properties.LastModified.HasValue &&
                    blob.Properties.LastModified.Value < DateTime.UtcNow.Subtract(maxAge))
                {
                    var v = (someUpdateMessagePosition.Value, blob.Properties.LastModified.Value);
                    items.Add(v);
                }
            }

            return items
                .OrderBy(i => i.Item2)
                .Where(i => i.Item1.Item % 10 == 0)
                .SkipLast(10);
        }

        private async Task<IEnumerable<string>> GetBlobNames(CancellationToken cancellationToken)
        {
            var blobs = this.snapshotContainerClient.GetBlobsAsync(cancellationToken: cancellationToken);
            var items = new List<string>();
            await foreach (var blob in blobs)
            {
                items.Add(blob.Name);
            }

            return items;
        }

        private async Task<FSharpOption<(MessagePosition, string)>> GetLatestSnapshotID(CancellationToken cancellationToken)
        {
            var names = await this.GetBlobNames(cancellationToken);
            var items = new List<(MessagePosition, string)>();

            foreach (var name in names)
            {
                var someUpdateMessagePosition = ParseMessagePositionFromBlobName(name);

                if (FSharpOption<MessagePosition>.get_IsSome(someUpdateMessagePosition))
                {
                    var v = (someUpdateMessagePosition.Value, name);
                    items.Add(v);
                }
            }

            if (items.Count == 0)
            {
                return FSharpOption<(MessagePosition, string)>.None;
            }

            return FSharpOption<(MessagePosition, string)>.Some(items.OrderByDescending(_ => _.Item1).First());
        }

        private static FSharpOption<MessagePosition> ParseMessagePositionFromBlobName(string n)
            => long.TryParse(n.Replace(".json", string.Empty), out long MessagePosition)
               ? FSharpOption<MessagePosition>.Some(MessagePosition.NewMessagePosition(MessagePosition))
               : FSharpOption<MessagePosition>.None;

        private static string MessagePositionToBlobName(MessagePosition position) => $"{position.SequenceNumber}.json";

        private static string MessagePositionToBlobName(MeteringState businessData) => MessagePositionToBlobName(businessData.LastProcessedMessage);
    }


    public static class SerializationExtensions
    {
        public static string ToUTF8String(this byte[] bytes) => Encoding.UTF8.GetString(bytes);

        public static byte[] ToUTF8Bytes(this string str) => Encoding.UTF8.GetBytes(str);

        public static string AsJSON<T>(this T t) => JsonConvert.SerializeObject(t);

        public static Stream AsJSONStream<T>(this T t) => new MemoryStream(t.AsJSON().ToUTF8Bytes());

        public static T DeserializeJSON<T>(this string s) => JsonConvert.DeserializeObject<T>(s);

        public static async Task<T> ReadJSON<T>(this Stream stream)
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            byte[] bytes = ms.ToArray();
            string s = bytes.ToUTF8String();
            return JsonConvert.DeserializeObject<T>(s, new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
            });
        }

        public static Stream GZipCompress(this Stream input)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress))
            {
                input.CopyTo(gzip);
            }

            return new MemoryStream(output.ToArray());
        }

        public static Stream GZipDecompress(this Stream input)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            {
                gzip.CopyTo(output);
            }

            return new MemoryStream(output.ToArray());
        }
    }
}
```

## fsharp trying to handle the AsyncAwaitables from the Azure SDK

```fsharp
let private asyncForeach (handler: ('t -> Task<Unit>)) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) (asncEnumerable: IAsyncEnumerable<'t>) : Task<Unit> =
    task { 
        let asyncEnumerator = asncEnumerable.GetAsyncEnumerator(cancellationToken = cancellationToken)
            
        let! h = asyncEnumerator.MoveNextAsync()
        let mutable hasNext = h
        while hasNext do
            let! _ = handler(asyncEnumerator.Current)

            let! h = asyncEnumerator.MoveNextAsync()
            hasNext <- h

        return ()
    }

let GetBlobNames (snapshotContainerClient: BlobContainerClient) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) =
    task {
        let blobs = snapshotContainerClient.GetBlobsAsync(cancellationToken = cancellationToken)
        let n = blobs.GetAsyncEnumerator(cancellationToken = cancellationToken)
        let resultList = new List<string>()
        let! h = n.MoveNextAsync()
        let mutable hasNext = h
        while hasNext do
            let item = n.Current
            resultList.Add(item.Properties.CreatedOn.ToString())
            let! h = n.MoveNextAsync()
            hasNext <- h
        return resultList
    }
```


