using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Metering.Types;

// https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(AggregatorFunctionHost.AggregatorStartup))]

namespace AggregatorFunctionHost
{
    public class AggregatorStartup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.RegisterMeteringAggregator();
        }
    }

    public static class AggregatorWorkerExtensions
    {
        public static void RegisterMeteringAggregator(this IServiceCollection services)
        {
            services.AddSingleton(MeteringConfigurationProviderModule.create(
                connections: MeteringConnectionsModule.getFromEnvironment(),
                marketplaceClient: MarketplaceClient.submitUsagesCsharp.ToFSharpFunc()));
        }
    }
}