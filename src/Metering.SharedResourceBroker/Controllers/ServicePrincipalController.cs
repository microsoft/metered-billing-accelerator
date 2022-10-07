namespace Metering.SharedResourceBroker.Controllers;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Threading.Tasks;
using Metering.SharedResourceBroker;

//[Authorize]
[ApiController]
[Route("[controller]")]
public class ServicePrincipalController : ControllerBase
{
    private readonly IOptions<ServicePrincipalCreatorSettings> _appSettings;
    private readonly IApplicationService _applicationService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ServicePrincipalController> _logger;

    public ServicePrincipalController(
        ILogger<ServicePrincipalController> logger, 
        IOptions<ServicePrincipalCreatorSettings> settingsOptions, 
        IApplicationService applicationService, 
        IConfiguration configuration)
    {
        _logger = logger;
        _appSettings = settingsOptions;
        _applicationService = applicationService;
        _configuration = configuration;
    }
    
    private bool IsAuthorized(SubscriptionRegistrationRequest o)
    {
        string secretPassed = Request.Headers.Authorization.ToString();
        return !string.IsNullOrEmpty(secretPassed) && secretPassed == _configuration["BootstrapSecret"];
    }

    [SwaggerResponse(StatusCodes.Status200OK, "Service principal created", typeof(SubscriptionRegistrationOkResponse))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "Service principal already exist", typeof(SubscriptionRegistrationFailedResponse))]
    [HttpPost]
    [Route("/Subscription")]
    public async Task<IActionResult> PostSubscription(SubscriptionRegistrationRequest o)
    {
        try
        {
            if (!IsAuthorized(o)) { return Unauthorized(); }
            return Ok(await _applicationService.CreateServicePrincipal(o));
        }
        catch (ArgumentNullException e) { return BadRequest(new SubscriptionRegistrationFailedResponse(Message: e.Message)); }
        catch (ArgumentException e) when (e.Message.Contains("Service principal already exist")) { return Conflict(new SubscriptionRegistrationFailedResponse(Message: e.Message)); }
        catch (Exception e) { return BadRequest(new SubscriptionRegistrationFailedResponse(Message: e.Message)); }
    }

    [HttpPost]
    [Route("/CreateServicePrincipalInKeyVault")]
    public async Task<IActionResult> CreateServicePrincipalInKeyVault(SubscriptionRegistrationRequest o)
    {
        try
        {
            if (!IsAuthorized(o)) { return Unauthorized(); }
            return Ok(await _applicationService.CreateServicePrincipalInKeyVault(o));
        }
        catch (ArgumentNullException e) { return BadRequest(new SubscriptionRegistrationFailedResponse(Message: e.Message)); }
        catch (ArgumentException e) when (e.Message.Contains("Service principal already exist")) { return Conflict(new SubscriptionRegistrationFailedResponse(Message: e.Message)); }
        catch (Exception e) { return BadRequest(new SubscriptionRegistrationFailedResponse(Message: e.Message)); }
    }
}
