namespace ManagedWebhook
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
    using System.Web;
    using Microsoft.Azure.WebJobs;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;

    public record DimensionConfig(string Dimension, double Quantity);

    public record Plan(string Publisher, string Product, string Name, string Version);

    public record ResourceGroupDefinition(string ManagedBy);

    public record UsageEventDefinition(string ResourceId, double Quantity, string Dimension, DateTime EffectiveStartTime, string PlanId);

    public record TokenDefinition(string Access_token);

    public record BillingDetailsDefinition(string ResourceUsageId);

    public record ApplicationProperties(BillingDetailsDefinition BillingDetails);

    public record ApplicationDefinition(ApplicationProperties Properties, Plan Plan);

    public enum ExecutionMode { Production, Local }

    public record Config (string MSIEndpoint, string MSISecret, ExecutionMode ExecutionMode, string ResourceGroupID, string MarketplaceURI, DimensionConfig[] Dimensions);

    public static class ChronJob
    {
        [FunctionName("ChronJob")]
        public static async Task Run(
            //[TimerTrigger("0 0 */1 * * *")] TimerInfo timerInfo,
            [TimerTrigger("* * * * * *")] TimerInfo timerInfo,
            ILogger log,
            ExecutionContext context)
        {
            log.LogCritical("Fuuuck");
            Config config = GetAppConfig(context);
            log.LogTrace($"Dimension configs: {JsonConvert.SerializeObject(config.Dimensions)}");

            using HttpClient armHttpClient = await CreateClient(config, log);

            // Determine managedBy
            var resourceGroupResponse = await armHttpClient.GetAsync($"https://management.azure.com{config.ResourceGroupID}?api-version=2019-11-01").ConfigureAwait(continueOnCapturedContext: false);
            if (resourceGroupResponse?.IsSuccessStatusCode == false)
            {
                log.LogError($"Failed to get the resource group from ARM. Error: {resourceGroupResponse.Content.ReadAsStringAsync().Result}");
            }
            var resourceGroup = await resourceGroupResponse.Content.ReadAsAsync<ResourceGroupDefinition>().ConfigureAwait(continueOnCapturedContext: false);
            var applicationResourceId = resourceGroup?.ManagedBy;
            if (string.IsNullOrEmpty(applicationResourceId))
            {
                log.LogError("The managedBy property either empty or missing for resource group.");
            }

            // determine application details
            var applicationResponse = await armHttpClient.GetAsync($"https://management.azure.com{applicationResourceId}?api-version=2019-07-01").ConfigureAwait(continueOnCapturedContext: false);
            if (applicationResponse?.IsSuccessStatusCode == false)
            {
                log.LogError("Failed to get the appplication from ARM.");
                return;
            }
            var application = await applicationResponse.Content.ReadAsAsync<ApplicationDefinition>().ConfigureAwait(continueOnCapturedContext: false);
            if (application == null)
            {
                return;
            }

            log.LogInformation($"Resource usage id: {application.Properties.BillingDetails?.ResourceUsageId}");
            log.LogInformation($"Plan name: {application.Plan.Name}");

            // This is where the actual stuff happens. This code just takes static config sample data and submits it...
            foreach (var dimensionConfig in config.Dimensions)
            {
                var usageEvent = new UsageEventDefinition(
                    ResourceId: application.Properties.BillingDetails?.ResourceUsageId,
                    Quantity: dimensionConfig.Quantity,
                    Dimension: dimensionConfig.Dimension, 
                    EffectiveStartTime: DateTime.UtcNow,
                    PlanId: application.Plan.Name);

                var response = config.ExecutionMode switch {
                    ExecutionMode.Production => await armHttpClient.PostAsJsonAsync(config.MarketplaceURI, usageEvent).ConfigureAwait(continueOnCapturedContext: false),
                    _ => new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent(JsonConvert.SerializeObject(usageEvent), UnicodeEncoding.UTF8, "application/json") },
                };

                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);                 
                if (response.IsSuccessStatusCode)
                {
                    log.LogTrace($"Successfully emitted a usage event. Reponse body: {responseBody}");
                }
                else
                {
                    log.LogError($"Failed to emit a usage event. Error code: {response.StatusCode}. Failure cause: {response.ReasonPhrase}. Response body: {responseBody}");
                }
            }
        }

        static Config GetAppConfig(ExecutionContext context)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            static ExecutionMode determine(string localRun) => localRun?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ? ExecutionMode.Local : ExecutionMode.Production;

            Config c = new(
                config["MSI_ENDPOINT"],
                config["MSI_SECRET"],
                determine(config["LOCAL_RUN"]),
                config["RESOURCEGROUP_ID"],
                config["MARKETPLACEAPI_URI"],
                JsonConvert.DeserializeObject<DimensionConfig[]>(config["DIMENSION_CONFIG"]));

            return c;
        }

        static async Task<HttpClient> CreateClient(Config config, ILogger log)
        {
            async Task<string> FetchProductionToken(HttpClient client, string resource)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{config.MSIEndpoint}/?resource={HttpUtility.UrlEncode(resource)}&api-version=2017-09-01");
                request.Headers.Add("Secret", config.MSISecret);
                var response = await client.SendAsync(request).ConfigureAwait(continueOnCapturedContext: false);
                if (response?.IsSuccessStatusCode == false)
                {
                    var e = await response.Content.ReadAsStringAsync();
                    log.LogError($"Failed to get token for system-assigned MSI. Please check that the MSI is set up properly. Error: {e}");
                }
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
                return JsonConvert.DeserializeObject<TokenDefinition>(responseBody).Access_token;
            };

            var armHttpClient = HttpClientFactory.Create();
            string armToken = config.ExecutionMode switch {
                ExecutionMode.Production => await FetchProductionToken(armHttpClient, "https://management.core.windows.net/"),
                _  => "token",
            };
            armHttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {armToken}");
            log.LogInformation($"Authorization bearer token: {armToken}");
            return armHttpClient;
        }
    }
}