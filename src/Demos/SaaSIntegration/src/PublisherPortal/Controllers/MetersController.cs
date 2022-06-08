using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using PublisherPortal.Services;
namespace PublisherPortal.Controllers;

[Route("~/Meters")]
public class MetersController : Controller
{
    private IConfiguration _configuration;


    public MetersController(

        IConfiguration Configuration)
    {

        _configuration = Configuration;
    }

    public async Task<ActionResult> IndexAsync()
    {
         var processMetered = new ProcessLatestMetered(_configuration);
        var model = await processMetered.GetLatestMetered("");
        return View(model);
    }
}