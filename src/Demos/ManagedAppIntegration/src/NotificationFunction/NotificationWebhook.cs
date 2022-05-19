using ManagedWebhook.Definitions;
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
using Metering.BaseTypes;
using Metering.ClientSDK;
using Metering.Integration;



namespace ManagedWebhook
{
    //The goal from this function to create subscription
    public static class NotificationWebhook
    {
        static async Task<T> readJson<T>(string name) => Json.fromStr<T>(await File.ReadAllTextAsync(name));

        [FunctionName("NotificationWebhook")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "resource")] HttpRequest req,
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
            using (var streamReader = new StreamReader(req.Body))
            {
                var requestBody = await streamReader
                    .ReadToEndAsync()
                    .ConfigureAwait(continueOnCapturedContext: false);

                log.LogTrace($"Notification payload: {requestBody}");

                var deserializationErrors = new List<string>();

                var notificationDefinition = JsonConvert.DeserializeObject<NotificationDefinition>(
                    value: requestBody,
                    settings: new JsonSerializerSettings
                    {
                        Error = delegate (object sender, ErrorEventArgs args)
                        {
                            deserializationErrors.Add(args.ErrorContext.Error.Message);
                            args.ErrorContext.Handled = true;
                        }
                    });

                if (notificationDefinition == null || deserializationErrors.Any())
                {
                    return new BadRequestObjectResult($"Failed to deserialize request body. Errors: {String.Join(';',deserializationErrors)}");
                }

                if (notificationDefinition.Plan != null)
                {
                    // If provisioning of a marketplace application instance is successful, we persist a billing entry to be picked up by the chron metric emitting job
                    if (notificationDefinition.EventType == "PUT" && notificationDefinition.ProvisioningState == "Succeeded" && notificationDefinition.BillingDetails?.ResourceUsageId != null)
                    {
                        var planPath = config["LOCAL_PATH"] + "plan.json";
                        var dimPath = config["LOCAL_PATH"] + "mapping.json";

                        var eventHubProducerClient = MeteringConnections.createEventHubProducerClientForClientSDK();
                        System.Threading.CancellationTokenSource cts = new System.Threading.CancellationTokenSource();
                        
                        var sub = new SubscriptionCreationInformation(
                        internalMetersMapping: await readJson<InternalMetersMapping>(dimPath),
                        subscription: new Subscription(
                            plan: await readJson<Metering.BaseTypes.Plan>(planPath),
                            internalResourceId: InternalResourceId.fromStr(notificationDefinition.ApplicationId),
                            renewalInterval: RenewalInterval.Monthly,
                            subscriptionStart: MeteringDateTimeModule.now()));


                        log.LogTrace(Json.toStr(1, sub));
                        await eventHubProducerClient.SubmitSubscriptionCreationAsync(sub, cts.Token);


                        log.LogTrace($"Successfully Subscribed managed app {notificationDefinition.ApplicationId}");
                    }
                    else if (notificationDefinition.EventType == "DELETE" && notificationDefinition.ProvisioningState == "Deleted" && notificationDefinition.Plan != null)
                    {
                        // Implement Remove from Metered Billing
                    }
                }
            }

            return new OkResult();
        }
    }
}
