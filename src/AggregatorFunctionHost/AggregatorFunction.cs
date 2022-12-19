// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

// https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: Microsoft.Azure.Functions.Extensions.DependencyInjection.FunctionsStartup(typeof(AggregatorFunctionHost.AggregatorStartup))]

namespace AggregatorFunctionHost;

using System;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Metering.Integration;
using Metering.RuntimeCS;

public class AggregatorStartup : FunctionsStartup
{
    public override void Configure(IFunctionsHostBuilder builder)
    {
        builder.Services.AddMeteringAggregatorConfigFromEnvironment();
    }
}

public class AggregatorFunction
{
    private readonly MeteringConfigurationProvider cfg;
    private readonly AggregationWorker aw;
    
    public AggregatorFunction(ILogger<AggregationWorker> l, MeteringConfigurationProvider c) { (cfg, aw) = (c, new(l, c)); }

    [FunctionName("AggregatorFunction")]
    public void Run([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, ILogger<AggregationWorker> logger, CancellationToken cancellationToken)
    {
        var token = cancellationToken.CancelAfter(TimeSpan.FromMinutes(3));

        logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}, Using event hub {cfg.MeteringConnections.EventHubConfig.EventHubName}");
        try
        {
            aw.ExecuteAsync(token).Wait(token);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Operation cancelled");
        }
    }
}