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
        public async Task SubmitAsync(string dimension, int unit)
        {
            await Task.Delay(1);

            InternalUsageEvent _ = new(
                scope: SubscriptionType.ManagedApp,
                timestamp: new ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc), 
                meterName: "meter1",
                quantity: QuantityModule.createInt(2),
                properties: FSharpOption<FSharpMap<string, string>>.None);
        }
    }
}