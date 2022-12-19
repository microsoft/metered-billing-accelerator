// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

// https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: Microsoft.Azure.Functions.Extensions.DependencyInjection.FunctionsStartup(typeof(AggregatorFunctionHost.AggregatorStartup))]

namespace AggregatorFunctionHost;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive.Linq;
using Microsoft.Azure.WebJobs;   
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Metering.ClientSDK;
using Metering.BaseTypes;
using Metering.BaseTypes.EventHub;
using Metering.Integration;
using Metering.EventHub;
using SomeMeterCollection = Microsoft.FSharp.Core.FSharpOption<Metering.BaseTypes.MeterCollection>;
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
    private readonly MeteringConfigurationProvider _meteringConfigurationProvider;
    private readonly AggregationWorker _aggregationWorker;
    
    public AggregatorFunction(ILogger<AggregationWorker> logger, MeteringConfigurationProvider meteringConfigurationProvider)
    {
        this._meteringConfigurationProvider = meteringConfigurationProvider;
        this._aggregationWorker = new(logger, meteringConfigurationProvider);
    }

    [FunctionName("AggregatorFunction")]
    public void Run([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, ILogger<AggregationWorker> logger, CancellationToken cancellationToken)
    {
        var token = cancellationToken.CancelAfter(TimeSpan.FromMinutes(3));

        logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}, Using event hub {_meteringConfigurationProvider.MeteringConnections.EventHubConfig.EventHubName}");
        try
        {
            _aggregationWorker.ExecuteAsync(token).Wait(token);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Operation cancelled");
        }
    }

}
