namespace MeteredPage.Controllers;

using MeteredPage.ViewModels.Home;
using Metering.ClientSDK;
using Metering.Integration;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using System.Threading;
using System.Threading.Tasks;

[Authorize(AuthenticationSchemes = OpenIdConnectDefaults.AuthenticationScheme)]
[AuthorizeForScopes(Scopes = new string[] { "user.read" })]
public class HomeController : Controller
{
    private readonly GraphServiceClient _graphServiceClient;
    private readonly IConfiguration _configuration;

    public HomeController(
        GraphServiceClient graphServiceClient,
        IConfiguration Configuration)
    {
        _graphServiceClient = graphServiceClient;
        _configuration = Configuration;
    }

    /// <summary>
    /// Shows all information associated with the user, the request, and the subscription.
    /// </summary>
    /// <param name="token">THe marketplace purchase ID token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns></returns>
    public async Task<IActionResult> IndexAsync(string token, CancellationToken cancellationToken)
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
        // get graph current user data
        var graphApiUser = await _graphServiceClient.Me.Request().GetAsync();

        return View(new IndexViewModel());
    }


    public async Task SendMeteredQty(double qtny)
    {
        // Subscription ID
        var resourceId = _configuration["SaasSubscription"];
        string dimensions = _configuration["SaaSDimension"];

        // Get Dim
        var eventHubProducerClient = MeteringConnections.createEventHubProducerClientForClientSDK();
        var meters = dimensions.Split(",");

        foreach (var dim in meters)
        {
            await eventHubProducerClient.SubmitMeterAsync(
                resourceId: resourceId,
                applicationInternalMeterName: dim,
                quantity: qtny);
        }
    }
}