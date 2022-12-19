// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Aggregator;

using Metering.Integration;
using Metering.RuntimeCS;

public class AggregatorEntryPoint
{
    class Worker : BackgroundService
    {
        private readonly AggregationWorker aw;

        public Worker(ILogger<AggregationWorker> l, MeteringConfigurationProvider c) { aw = new(l, c); }

        protected override Task ExecuteAsync(CancellationToken ct) => aw.ExecuteAsync(ct);        
    }

    public static Task Main(string[] args) => Host
        .CreateDefaultBuilder(args)
        .ConfigureServices(services =>
        {
            services.AddMeteringAggregatorConfigFromEnvironment();
            services.AddHostedService<Worker>();
        })
        .Build()
        .RunAsync();
}