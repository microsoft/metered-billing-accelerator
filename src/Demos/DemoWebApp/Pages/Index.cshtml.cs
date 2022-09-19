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
        public Task OnPostNodeChargeAsync(CancellationToken ct) => Submit("nde", 1.0, ct);
        public Task OnPostCpuChargeAsync(CancellationToken ct) => Submit("cpu", 1.0, ct);
        public Task OnPostDataSourceChargeAsync(CancellationToken ct) => Submit("dta", 1.0, ct);
        public Task OnPostMessageChargeAsync(CancellationToken ct) => Submit("msg", 1.0, ct);
        public Task OnPostObjectChargeAsync(CancellationToken ct) => Submit("obj", 1.0,  ct);

        const string resourceId = "fdc778a6-1281-40e4-cade-4a5fc11f5440";

        private async Task Submit(string applicationInternalName, double amount, CancellationToken ct)
        {
            await _eventHubProducerClient.SubmitMeterAsync(
                  resourceId: resourceId,
                  applicationInternalMeterName: applicationInternalName,
                  quantity: amount,
                  cancellationToken: ct);

            //await _eventHubProducerClient.SubmitManagedAppMeterAsync(
            //    MeterValues.create(applicationInternalName, amount), ct);

            var msg = $"Submitted consumption of {applicationInternalName}={amount} ";

            Msg = msg;
            _logger.LogInformation(msg);
        }
    }
}