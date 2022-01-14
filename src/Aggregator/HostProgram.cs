using Metering.Aggregator;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.RegisterMeteringAggregator();
    })
    .Build();

await host.RunAsync();
