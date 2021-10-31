namespace Metering.ClientSDK
{
    using System.Threading.Tasks;
    using Microsoft.FSharp.Collections;
    using Microsoft.FSharp.Core;
    using NodaTime;
    using Metering.Types;
    using static Metering.Types.MarketPlaceAPI;

    public class Client
    {

        public async Task SubmitManagedAppIntegerAsync(string meterName, ulong unit)
        {
            await Task.Delay(1);

            InternalUsageEvent _ = new(
                scope: SubscriptionType.ManagedApp,
                timestamp: new ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc),
                meterName: meterName,
                quantity: QuantityModule.createInt(unit),
                properties: FSharpOption<FSharpMap<string, string>>.None);
        }

        public async Task SubmitManagedAppFloatAsync(string meterName, decimal unit)
        {
            await Task.Delay(1);

            InternalUsageEvent _ = new(
                scope: SubscriptionType.ManagedApp,
                timestamp: new ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc),
                meterName: meterName,
                quantity: QuantityModule.createFloat(unit),
                properties: FSharpOption<FSharpMap<string, string>>.None);
        }

        public async Task SubmitSaasIntegerAsync(string saasId, string meterName, ulong unit)
        {
            await Task.Delay(1);

            InternalUsageEvent _ = new(
                scope: SubscriptionType.NewSaaSSubscription(SaaSSubscriptionIDModule.create(saasId)),
                timestamp: new ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc),
                meterName: meterName,
                quantity: QuantityModule.createInt(unit),
                properties: FSharpOption<FSharpMap<string, string>>.None);
        }

        public async Task SubmitSaasFloatAsync(string saasId, string meterName, decimal unit)
        {
            await Task.Delay(1);

            InternalUsageEvent _ = new(
                scope: SubscriptionType.NewSaaSSubscription(SaaSSubscriptionIDModule.create(saasId)),
                timestamp: new ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc),
                meterName: meterName,
                quantity: QuantityModule.createFloat(unit),
                properties: FSharpOption<FSharpMap<string, string>>.None);
        }
    }
}