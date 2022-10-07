namespace ManagedWebhook;

using ManagedWebhook.Definitions;
using Metering.BaseTypes;
using Metering.ClientSDK;
using Metering.Integration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ErrorEventArgs = Newtonsoft.Json.Serialization.ErrorEventArgs;

//The goal from this function to create subscription
public static class BillingMeteredWebhook
{
    static async Task<T> readJson<T>(string name) => Json.fromStr<T>(await File.ReadAllTextAsync(name));

    [FunctionName("BillingMeteredWebhook")]
    public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "billing")] HttpRequest req,
        ILogger log,
        ExecutionContext context)
    {
        // Authorize the request based on the sig parameter
        var sig = req.Query["sig"];

        var config = new ConfigurationBuilder()
            .SetBasePath(context.FunctionAppDirectory)
            .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        if (!config["URL_SIGNATURE"].Equals(sig, StringComparison.OrdinalIgnoreCase))
        {
            log.LogError($"Unexpected or missing 'sig' parameter value '{sig}'");
            return new UnauthorizedResult();
        }

        using var streamReader = new StreamReader(req.Body);

        var requestBody = await streamReader
            .ReadToEndAsync()
            .ConfigureAwait(continueOnCapturedContext: false);

        log.LogTrace($"Notification payload: {requestBody}");

        var deserializationErrors = new List<string>();

        var meteredUsage = JsonConvert.DeserializeObject<UsageEventDefinition>(
            value: requestBody,
            settings: new JsonSerializerSettings
            {
                Error = delegate (object sender, ErrorEventArgs args)
                {
                    deserializationErrors.Add(args.ErrorContext.Error.Message);
                    args.ErrorContext.Handled = true;
                }
            });

        if (meteredUsage == null || deserializationErrors.Any())
        {
            return new BadRequestObjectResult($"Failed to deserialize request body. Errors: {String.Join(';', deserializationErrors)}");
        }

        if (meteredUsage.ResourceId != null)
        {
            var eventHubProducerClient = MeteringConnections.createEventHubProducerClientForClientSDK();
            log.LogTrace($"Initiate eventhub client");

            log.LogTrace($"Emitting Quantity {meteredUsage.Quantity} of Dim {meteredUsage.Dimension} to ResourceID {meteredUsage.ResourceId}");
            await eventHubProducerClient.SubmitMeterAsync(
                resourceId: meteredUsage.ResourceId,
                applicationInternalMeterName: meteredUsage.Dimension,
                quantity: meteredUsage.Quantity
                );

            log.LogTrace($"Successfully Subscribed managed app {meteredUsage.ResourceId} with Qty {meteredUsage.Quantity}");
        }

        return new OkResult();
    }
}