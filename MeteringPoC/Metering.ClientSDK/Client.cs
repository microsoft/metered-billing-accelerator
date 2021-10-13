namespace Metering.ClientSDK
{
    using Microsoft.FSharp.Collections;
    using Microsoft.FSharp.Core;
    using System;
    using System.Threading.Tasks;
    using Types;

    public class Client
    {
        public async Task SubmitAsync(string dimension, int unit)
        {
            await Task.Delay(1);

            UsageEvent _ = new(
                timestamp: DateTime.UtcNow, 
                meterName: "meter1",
                quantity: 2,
                properties: FSharpOption<FSharpMap<string, string>>.None);
        }
    }
}