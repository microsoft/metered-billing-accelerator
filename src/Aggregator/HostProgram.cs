// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Metering.Aggregator;
using Metering.Integration;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddMeteringAggregatorConfigFromEnvironment();
        services.AddHostedService<AggregatorWorker>();
    })
    .Build();

await host.RunAsync();
