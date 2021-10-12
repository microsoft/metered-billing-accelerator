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
                planID: "someplan",
                timestamp: DateTime.UtcNow,
                dimension: "dimension",
                quantity: 2,
                properties: FSharpOption<FSharpMap<string, string>>.None);
        }
    }
}