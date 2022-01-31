// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace DemoWebApp.Pages
{
    using Microsoft.AspNetCore.Mvc.RazorPages;
    using Azure.Messaging.EventHubs.Producer;
    using Metering.ClientSDK;
    
    public partial class IndexModel : PageModel
    {
        private readonly EventHubProducerClient _eventHubProducerClient;
        private readonly ILogger<IndexModel> _logger;
        public string Msg { get; set; } = string.Empty;

        public IndexModel(ILogger<IndexModel> logger, EventHubProducerClient eventHubProducerClient)
        {
            (_logger, _eventHubProducerClient) = (logger, eventHubProducerClient);
        }

        public void OnGet() { }
        public Task OnPostNodeChargeAsync(CancellationToken ct) => Submit(1.0, "nde", ct);
        public Task OnPostCpuChargeAsync(CancellationToken ct) => Submit(1.0, "cpu", ct);
        public Task OnPostDataSourceChargeAsync(CancellationToken ct) => Submit(1.0, "dta", ct);
        public Task OnPostMessageChargeAsync(CancellationToken ct) => Submit(1.0, "msg", ct);
        public Task OnPostObjectChargeAsync(CancellationToken ct) => Submit(1.0, "obj", ct);

        const string saasId = "fdc778a6-1281-40e4-cade-4a5fc11f5440";

        private async Task Submit(double amount, string name, CancellationToken ct)
        {
            await _eventHubProducerClient.SubmitSaaSMeterAsync(
                SaaSConsumptionModule.create(saasId, MeterValueModule.create(name, amount)), ct);
            
            //await _eventHubProducerClient.SubmitManagedAppMeterAsync(
            //    MeterValueModule.create(name, amount), ct);

            var msg = $"Submitted consumption of {amount} {name}";

            Msg = msg;
            _logger.LogInformation(msg);
        }
    }
}