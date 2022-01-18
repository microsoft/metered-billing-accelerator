using Metering.Aggregator;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.RegisterMeteringAggregator();
        services.AddHostedService<AggregatorWorker>();
    })
    .Build();

await host.RunAsync();
