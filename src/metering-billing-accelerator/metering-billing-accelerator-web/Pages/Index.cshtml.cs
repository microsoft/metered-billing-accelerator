using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace metering_billing_accelerator_web.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        public string Msg { get; set; }
        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {

        }

        public void OnPostWork1()
        {
            Msg = "Work 1";
        }

        public void OnPostWork2()
        {
            Msg = "Work 2";
        }

        public void OnPostWork3()
        {
            Msg = "Work 3";
        }

        public void OnPostWork4()
        {
            Msg = "Work 4";
        }

        public void OnPostWork5()
        {
            Msg = "Work 5";
        }
    }
}
