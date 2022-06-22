namespace MeteredPage.Controllers;

using MeteredPage.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

public class MeterController : Controller
{
    private readonly IConfiguration _configuration;

    public MeterController(IConfiguration Configuration)
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
