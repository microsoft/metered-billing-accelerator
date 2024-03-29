namespace Metering.SharedResourceBroker.Controllers;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Metering.SharedResourceBroker;
using Swashbuckle.AspNetCore.Annotations;

[ApiController]
[Route("[controller]")]
public class ServicePrincipalController : ControllerBase
{
    private readonly ApplicationService _applicationService;
    private readonly IConfiguration _configuration;

    public ServicePrincipalController(ApplicationService applicationService, IConfiguration configuration)
    {
        _applicationService = applicationService;
        _configuration = configuration;
    }
    
    [HttpPost]
    [Route("/CreateServicePrincipalInKeyVault")]
    [SwaggerResponse(StatusCodes.Status200OK, "Service principal created", typeof(CreateServicePrincipalInKeyVaultResponse))]
    public async Task<IActionResult> CreateServicePrincipalInKeyVault(SubscriptionRegistrationRequest subscriptionRegistrationRequest)
    {
        bool IsAuthorized()
        {
            string secretPassed = Request.Headers.Authorization.ToString();
            return !string.IsNullOrEmpty(secretPassed) && secretPassed == _configuration["BootstrapSecret"];
        }

        try
        {
            if (!IsAuthorized()) { return Unauthorized(); }
            return Ok(await _applicationService.CreateServicePrincipalInKeyVault(subscriptionRegistrationRequest));
        }
        catch (ArgumentNullException e) { return BadRequest(new SubscriptionRegistrationFailedResponse(Message: e.Message)); }
        catch (ArgumentException e) when (e.Message.Contains("Service principal already exist")) { return Conflict(new SubscriptionRegistrationFailedResponse(Message: e.Message)); }
        catch (Exception e) { return BadRequest(new SubscriptionRegistrationFailedResponse(Message: e.Message)); }
    }
}
