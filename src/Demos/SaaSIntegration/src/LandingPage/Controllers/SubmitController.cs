namespace LandingPage.Controllers;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize(AuthenticationSchemes = OpenIdConnectDefaults.AuthenticationScheme)]
public class SubmitController : Controller
{
    public IActionResult Index() => View();
}
