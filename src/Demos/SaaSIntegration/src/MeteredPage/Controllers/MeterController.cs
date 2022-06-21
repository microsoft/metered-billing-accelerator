using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Linq;
using Azure.Storage;
using System.Threading;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.IO.Compression;
using Newtonsoft.Json;
using Metering.BaseTypes;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MeteredPage.ViewModels.Meter;
using MeteredPage.Services;
namespace MeteredPage.Controllers
{
    public class MeterController : Controller
    {
        private IConfiguration _configuration;


        public MeterController(

            IConfiguration Configuration)
        {

            _configuration = Configuration;
        }
        // GET: MeterController
        public async Task<ActionResult> IndexAsync()
        {
            string saasSubscription = _configuration["SaasSubscription"];
            if (string.IsNullOrEmpty(saasSubscription))
            {
                this.ModelState.AddModelError(string.Empty, "Subscription Information is missing");
                this.ViewBag.Message = "Subscription Information is missing";
                return this.View();
            }

            string saasDimension = _configuration["SaaSDimension"];
            if (string.IsNullOrEmpty(saasDimension))
            {
                this.ModelState.AddModelError(string.Empty, "Dimension Information is missing");
                this.ViewBag.Message = "Dimension Information is missing";
                return this.View();
            }
            var processMetered = new ProcessLatestMetered(_configuration);
            var model = await processMetered.GetLatestMetered(saasSubscription);
            return View(model);
        }


 


  

    }
}
