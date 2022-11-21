namespace PublisherPortal.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PublisherPortal.Services;
using System.Threading.Tasks;

[Route("~/Meters")]
public class MetersController : Controller
{
    private IConfiguration _configuration;

    public MetersController(IConfiguration Configuration)
    {
        _configuration = Configuration;
    }

    public async Task<ActionResult> IndexAsync()
    {
        ProcessLatestMetered processMetered = new(_configuration);
        var model = await processMetered.GetLatestMetered("");
        return View(model);
    }
}