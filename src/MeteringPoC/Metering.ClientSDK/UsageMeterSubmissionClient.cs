namespace Metering.ClientSDK
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;    
    using Microsoft.FSharp.Collections;
    using Microsoft.FSharp.Core;
    using Azure.Messaging.EventHubs;
    using Azure.Messaging.EventHubs.Producer;
    using NodaTime;
    using Metering.Types;
    using static Metering.Types.MarketPlaceAPI;

    public class UsageMeterSubmissionClient
    {
        private readonly EventHubProducerClient producer;

        public UsageMeterSubmissionClient(EventHubProducerClient eventHubProducerClient)
        {
            this.producer = eventHubProducerClient;
        }

        public Task SubmitManagedAppIntegerAsync(string meterName, ulong unit, CancellationToken ct) => SubmitUsage(new(
                scope: SubscriptionType.ManagedApp,
                timestamp: new ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc),
                meterName: ApplicationInternalMeterNameModule.create(meterName),
                quantity: QuantityModule.createInt(unit),
                properties: FSharpOption<FSharpMap<string, string>>.None), ct);

        public Task SubmitManagedAppFloatAsync(string meterName, decimal unit, CancellationToken ct) => SubmitUsage(new(
                scope: SubscriptionType.ManagedApp,
                timestamp: new ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc),
                meterName: ApplicationInternalMeterNameModule.create(meterName),
                quantity: QuantityModule.createFloat(unit),
                properties: FSharpOption<FSharpMap<string, string>>.None), ct);

        public Task SubmitSaasIntegerAsync(string saasId, string meterName, ulong unit, CancellationToken ct) => SubmitUsage(new(
                scope: SubscriptionType.NewSaaSSubscription(SaaSSubscriptionIDModule.create(saasId)),
                timestamp: new ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc),
                meterName: ApplicationInternalMeterNameModule.create(meterName),
                quantity: QuantityModule.createInt(unit),
                properties: FSharpOption<FSharpMap<string, string>>.None), ct);

        public Task SubmitSaasFloatAsync(string saasId, string meterName, decimal unit, CancellationToken ct) => SubmitUsage(new(
                scope: SubscriptionType.NewSaaSSubscription(SaaSSubscriptionIDModule.create(saasId)),
                timestamp: new ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc),
                meterName: ApplicationInternalMeterNameModule.create(meterName),
                quantity: QuantityModule.createFloat(unit),
                properties: FSharpOption<FSharpMap<string, string>>.None), ct);

        private async Task SubmitUsage(InternalUsageEvent internalUsageEvent, CancellationToken ct)
        {
            await Task.Delay(1, ct);
            var eventBatch = await producer.CreateBatchAsync(
                options: new CreateBatchOptions
                {
                    // PartitionKey = Guid.NewGuid().ToString(),
                    // PartitionId = ids[id],
                },
                cancellationToken: ct
            );

            EventData eventData = new(new BinaryData(Json.toStr(internalUsageEvent)));

            //eventData.Properties.Add("SendingApplication", typeof(EventHubDemoProgram).Assembly.Location);
            if (!eventBatch.TryAdd(eventData))
            {
                throw new Exception($"The event could not be added.");
            }
            await producer.SendAsync(
                eventBatch: eventBatch,
                // options: new SendEventOptions() {  PartitionId = "...", PartitionKey = "..." },
                cancellationToken: ct);
        }
    }
}