namespace Metering.SharedResourceBroker;

using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

public class RequestIdentityLoggingMiddleware : IMiddleware
{
    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.User.Identity is { IsAuthenticated: true })
        {
            var requestTelemetry = context.Features.Get<RequestTelemetry>();
            var identity = context.User.Identity.Name;
            requestTelemetry?.Properties.Add("Identity", identity);
        }
        return next(context);
    }
}
