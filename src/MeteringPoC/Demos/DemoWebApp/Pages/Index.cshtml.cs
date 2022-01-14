namespace DemoWebApp.Pages
{
    using Microsoft.AspNetCore.Mvc.RazorPages;
    using Azure.Messaging.EventHubs.Producer;
    using Metering.ClientSDK;
    using Metering.Types;
    
    public partial class IndexModel : PageModel
    {
        private readonly EventHubProducerClient _eventHubProducerClient;
        private readonly ILogger<IndexModel> _logger;
        public string Msg { get; set; } = String.Empty;

        public IndexModel(ILogger<IndexModel> logger, EventHubProducerClient eventHubProducerClient)
        {
            (_logger, _eventHubProducerClient) = (logger, eventHubProducerClient);
        }

        public void OnGet() { }
        public Task OnPostNodeChargeAsync() => Submit(1.0, "nde");
        public Task OnPostCpuChargeAsync() => Submit(1.0, "cpu");
        public Task OnPostDataSourceChargeAsync() => Submit(1.0, "dta");
        public Task OnPostMessageChargeAsync() => Submit(1.0, "msg");
        public Task OnPostObjectChargeAsync() => Submit(1.0, "obj");

        private async Task Submit(double amount, string name)
        {
            await _eventHubProducerClient.SubmitManagedAppMeterAsync(new[] { new Metering.ClientSDK.MeterValue(
                quantity: Quantity.NewMeteringFloat(amount),
                name: ApplicationInternalMeterNameModule.create(name)) });
            var msg = $"Submitted consumption of {amount} {name}";

            Msg = msg;
            _logger.LogInformation(msg);
        }
    }
}