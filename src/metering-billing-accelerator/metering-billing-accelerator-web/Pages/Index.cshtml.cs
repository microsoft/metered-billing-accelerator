using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using metering_billing_accelerator_web;

namespace metering_billing_accelerator_web.Pages
{
    public partial class IndexModel : PageModel
    {
        private readonly MeteringService meteringService;

        private readonly ILogger<IndexModel> _logger;
        public string Msg { get; set; }
        public IndexModel(ILogger<IndexModel> logger, MeteringService meteringService) // points to the metering service for event hub.
        {
            _logger = logger;
            this.meteringService = meteringService;
        }

        public void OnGet()
        {

        }


        public async Task OnPostNodeChargeAsync()
        {
            await meteringService.EmitMeterAsync("sub1", "free", "nodeCharge", 1, "tag1");
            Msg = "nodeCharge emitted";
        }

        public async Task OnPostCpuChargeAsync()
        {
            await meteringService.EmitMeterAsync("sub1", "free", "cpuCharge", 1, "tag1");
            Msg = "cpuCharge emitted";
        }

        public async Task OnPostDataSourceChargeAsync()
        {
            await meteringService.EmitMeterAsync("sub1", "free", "dataSourceCharge", 1, "tag1");
            Msg = "dataSourceCharge emitted";
        }

        public async Task OnPostMessageChargeAsync()
        {
            await meteringService.EmitMeterAsync("sub1", "free", "messageCharge", 1, "tag1");
            Msg = "messageCharge emitted";
        }

        public async Task OnPostObjectChargeAsync()
        {
            await meteringService.EmitMeterAsync("sub1", "plan1", "objectCharge", 1, "tag1");
            Console.WriteLine("objectCharge emitted");
            Msg = "objectCharge emitted";
        }
    }
}
