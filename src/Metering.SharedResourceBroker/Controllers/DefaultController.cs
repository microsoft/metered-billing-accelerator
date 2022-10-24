namespace Metering.SharedResourceBroker.Controllers;

using Microsoft.AspNetCore.Mvc;

[ApiExplorerSettings(IgnoreApi = true)]
public class DefaultController : Controller
{
    [Route("/")]
    [Route("/docs")]
    [Route("/swagger")]
    public IActionResult Index()
    {
        return new RedirectResult("~/swagger");
    }
}
