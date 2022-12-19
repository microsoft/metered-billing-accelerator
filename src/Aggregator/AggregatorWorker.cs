// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Aggregator;

using Metering.Integration;
using Metering.RuntimeCS;

public class AggregatorWorker : BackgroundService
{
    private readonly AggregationWorker _aggregationWorker;

    public AggregatorWorker(ILogger<AggregationWorker> logger, MeteringConfigurationProvider meteringConfigurationProvider)
    {
        this._aggregationWorker = new (logger, meteringConfigurationProvider);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _aggregationWorker.ExecuteAsync(stoppingToken);    
}