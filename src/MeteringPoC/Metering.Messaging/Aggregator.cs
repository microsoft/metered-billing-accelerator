namespace Metering.Messaging
{
    using Azure.Messaging.EventHubs.Consumer;
    using Metering.Types;
    using Metering.Types.EventHub;
    using Microsoft.FSharp.Core;
    using NodaTime;
    using System.Reactive.Disposables;
    using System.Reactive.Linq;

    public class Aggregator
    {
        public static IObservable<(MeteringEvent, EventsToCatchup)> CreateObservable(
            DemoCredential config, FSharpOption<MessagePosition> someMessagePosition, CancellationToken cancellationToken = default)
        {
            var eventHubConsumerClient = new EventHubConsumerClient(
                consumerGroup: config.EventHubInformation.ConsumerGroup,
                fullyQualifiedNamespace: $"{config.EventHubInformation.EventHubNamespaceName}.servicebus.windows.net",
                eventHubName: config.EventHubInformation.EventHubInstanceName,
                credential: config.TokenCredential);

            return Observable.Create<(MeteringEvent, EventsToCatchup)>(o =>
            {
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