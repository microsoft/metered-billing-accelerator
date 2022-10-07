using Azure.Identity;
using Metering.SharedResourceBroker;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Swashbuckle.AspNetCore.Filters;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)    ;

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.Configure<ServicePrincipalCreatorSettings>(
    builder.Configuration.GetSection(ServicePrincipalCreatorSettings.Names.SectionName));
builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
    options.OperationFilter<AppendAuthorizeToSummaryOperationFilter>();
    options.OperationFilter<SecurityRequirementsOperationFilter>();
});
builder.Services.AddApplicationInsightsTelemetry(builder.Configuration);
//builder.Services.AddLogging(c => c.AddApplicationInsights());
builder.Services.AddTransient<RequestIdentityLoggingMiddleware>();
builder.Services.AddScoped<IApplicationService, ApplicationService>();

var keyVaultName = builder.Configuration
    .GetSection(ServicePrincipalCreatorSettings.Names.SectionName)
    .GetSection(ServicePrincipalCreatorSettings.Names.KeyVaultName).Value;
var managedIdentityClientId = builder.Configuration[
    string.Join(':',
        ServicePrincipalCreatorSettings.Names.SectionName,
        ServicePrincipalCreatorSettings.Names.AzureADManagedIdentityClientId)];

builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{keyVaultName}.vault.azure.net/"),
    new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ManagedIdentityClientId = managedIdentityClientId
    })
);

var app = builder.Build();

//if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RequestIdentityLoggingMiddleware>();
app.MapControllers();
app.Run();
