namespace LandingPage.Controllers;

using LandingPage.ViewModels.Unsubscribe;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Marketplace.SaaS;
using System;
using System.Threading;
using System.Threading.Tasks;

[Authorize(AuthenticationSchemes = OpenIdConnectDefaults.AuthenticationScheme)]
public class UnsubscribeController : Controller
{
    private readonly IMarketplaceSaaSClient _marketplaceSaaSClient;

    public UnsubscribeController(IMarketplaceSaaSClient marketplaceSaaSClient)
    {
        _marketplaceSaaSClient = marketplaceSaaSClient;
    }

    public async Task<IActionResult> IndexAsync(Guid id, CancellationToken cancellationToken)
    {
        var _operationId = await _marketplaceSaaSClient.Fulfillment.DeleteSubscriptionAsync(id, cancellationToken: cancellationToken);

        return this.RedirectToAction("Deleted", new { id } );
    }

    public async Task<IActionResult> Deleted(Guid id, CancellationToken cancellationToken)
    {
        var subscription = (await _marketplaceSaaSClient.Fulfillment.GetSubscriptionAsync(id, cancellationToken: cancellationToken)).Value;

        var model = new DeleteViewModel
        {
            Subscription = subscription
        };

        return View(model);
    }
}