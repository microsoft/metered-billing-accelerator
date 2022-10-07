namespace Metering.SharedResourceBroker.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using Metering.SharedResourceBroker;

[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/[controller]")]
[ApiController]
/*
 * https://docs.microsoft.com/en-us/azure/azure-resource-manager/managed-applications/publish-notifications#azure-marketplace-application-notification-schema
 */
public class ResourceController : ControllerBase
{
    private readonly ILogger<ResourceController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IApplicationService _applicationService;

    public ResourceController(ILogger<ResourceController> logger, IConfiguration configuration, IApplicationService applicationService)
    {
        _logger = logger;
        _configuration = configuration;
        _applicationService = applicationService;
    }

    [ApiExplorerSettings(IgnoreApi = true)]
    [HttpPost]
    [Route("/resource")]
    public IActionResult ApplicationNotification(string sig)
    {
        var sec = _configuration["NotificationSecret"];
        if (sig != sec)
        {
            _logger.LogError("Marketplace attempt with wrong signature");
            return Unauthorized();
        }
        try
        {
            string json = new StreamReader(this.Request.BodyReader.AsStream()).ReadToEnd();
            var data = json.DeserializeJson<CommonNotificationInformation>();

            _logger.LogDebug(json);

            switch (data.ProvisioningState)
            {
                case (ProvisioningState.Succeeded):
                    _logger.LogDebug("Succeeded ");
                    break;
                case (ProvisioningState.Deleting):
                    _logger.LogDebug("Deleting");
                    break;
                case (ProvisioningState.Deleted):
                    _applicationService.DeleteApplication(data.ApplicationId);
                    _logger.LogDebug("Deleted");
                    break;
                default:
                    break;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message, e);
            return BadRequest(e.Message);
        }
        return Ok();
    }
}