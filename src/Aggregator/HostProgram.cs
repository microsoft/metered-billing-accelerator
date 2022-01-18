using Metering.Aggregator;
using Metering.Types;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddMeteringAggregatorConfigFromEnvironment();
        services.AddHostedService<AggregatorWorker>();
    })
    .Build();

await host.RunAsync();
