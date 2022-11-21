namespace Metering.SharedResourceBroker;

using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Newtonsoft.Json;
using SimpleBase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class ApplicationService
{
    private readonly ILogger<ApplicationService> _logger;
    private readonly ServicePrincipalCreatorSettings _appSettings;
    private readonly SecretClient _keyVaultSecretClient;

    public ApplicationService(ILogger<ApplicationService> logger, IOptions<ServicePrincipalCreatorSettings> settingsOptions)
    {
        _logger = logger;
        _appSettings = settingsOptions.Value;

        _keyVaultSecretClient = new SecretClient(
            vaultUri: new($"https://{_appSettings.KeyVaultName}.vault.azure.net/"),
            credential: new DefaultAzureCredential(
                new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = _appSettings.AzureADManagedIdentityClientId
                }));
    }

    private string GetApplicationName(string managedBy)
    {
        // managedBy = "/subscriptions/.../resourceGroups/.../providers/microsoft.solutions/applications/..."
        //var parts = managedBy.TrimStart('/').Split('/');
        //var (subscriptionId, managedAppResourceGroup, managedAppName) = (parts[1], parts[3], parts[7]);
        //return $"{_appSettings.Value.GeneratedServicePrincipalPrefix}-{subscriptionId}-{managedAppResourceGroup}-{managedAppName}";

        var hash = MD5.HashData(Encoding.UTF8.GetBytes(managedBy));
        var hashBase32 = Base32.Crockford.Encode(hash);
        return $"{_appSettings.GeneratedServicePrincipalPrefix}-{hashBase32}"; // The result must have a length of at most 93
    }

    private GraphServiceClient GetGraphServiceClient()
    {
        var credential = new ChainedTokenCredential(
            new ManagedIdentityCredential(_appSettings.AzureADManagedIdentityClientId.ToString()),
            new EnvironmentCredential()
        );

        var token = credential.GetToken(
            new Azure.Core.TokenRequestContext(
                new[] { "https://graph.microsoft.com/.default" }));

        var accessToken = token.Token;
        return new(
           new DelegateAuthenticationProvider((requestMessage) =>
           {
               requestMessage.Headers.Authorization = new(scheme: "Bearer", parameter: accessToken);
               return Task.CompletedTask;
           }));
    }

    public async Task DeleteApplication(string managedBy)
    {
        try
        {
            var queryOptions = new List<QueryOption>
            {
                new("$count", "true")
            };

            var appName = GetApplicationName(managedBy);
            var _graphServiceClient = GetGraphServiceClient();
            var applications = await _graphServiceClient.Applications
                .Request(queryOptions)
                .Filter($"startsWith(displayName,'{appName}')")
                .Header("ConsistencyLevel", "eventual")
                .GetAsync();
            if (applications.Any())
            {
                var application = applications.First();
                _logger.LogInformation($"Deleting app: {application.DisplayName}");
                await _graphServiceClient.Applications[application.Id].Request().DeleteAsync();
            }

            await DeleteServicePrincipalSecret(managedBy);
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message, e);
            throw;
        }
    }

    internal async Task<SubscriptionRegistrationOkResponse> CreateServicePrincipal(SubscriptionRegistrationRequest subscriptionRegistrationRequest)
    {
        subscriptionRegistrationRequest.EnsureValid();

        var appName = GetApplicationName(subscriptionRegistrationRequest.ManagedBy);
        try
        {
            GraphServiceClient _graphServiceClient = GetGraphServiceClient();

            //Search for AAD app. Make sure SP does not already exist
            var apps = await _graphServiceClient.Applications
                .Request(
                    new List<QueryOption>()
                    {
                        new("$count", "true"),
                        new("$filter", $"DisplayName eq '{appName}'")
                    })
                .Header("ConsistencyLevel", "eventual")
                .GetAsync();

            //Ensure that only one app can be created
            if (apps.Count > 0)
            {
                throw new ArgumentException("Service principal already exist");
            }

            //Create AAD application
            var app = await _graphServiceClient
                .Applications
                .Request()
                .AddAsync(new Application
                {
                    DisplayName = appName,
                    SignInAudience = "AzureADMyOrg",
                    Description = $"ManagedBy: {subscriptionRegistrationRequest.ManagedBy}",
                    Notes = $"ManagedBy: {subscriptionRegistrationRequest.ManagedBy}",
                });

            _logger.LogTrace($"AAD app created: {app.DisplayName}");

            //Create Secret
            var pwd = await _graphServiceClient.Applications[app.Id]
                .AddPassword(
                    new PasswordCredential
                    {
                        DisplayName = $"{_appSettings.GeneratedServicePrincipalPrefix}-rbac",
                        EndDateTime = DateTime.Now.AddYears(100),
                    })
                .Request()
                .PostAsync();
            _logger.LogTrace($"AAD app password created: {app.DisplayName}");

            //Create Service principal for app
            var spr = await _graphServiceClient.ServicePrincipals
                .Request()
                .AddAsync(new ServicePrincipal
                {
                    AppId = app.AppId,
                });
            _logger.LogTrace($"Service principal created: {spr.Id}");

            int retry = 10;
            for (int i = 0; i < retry; i++)
            {
                try
                {
                    //Add Service principal to the security group, which has permissions the resource(s).
                    await _graphServiceClient.Groups[_appSettings.SharedResourcesGroup.ToString()].Members.References
                         .Request()
                         .AddAsync(new DirectoryObject { Id = spr.Id });
                    _logger.LogTrace($"Service principal added to security group: {spr.Id}");
                    break;
                }
                catch (ServiceException e) when (e.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    throw;
                }
                catch (Exception e)
                {
                    if (i == retry)
                        _logger.LogError($"Failed to add service principal to group", e);
                    _logger.LogWarning($"Retry {i}", e);
                    Thread.Sleep(200 * (i + 1));
                }
            }

            _logger.LogDebug($"Setup completed for app {appName}");

            return new SubscriptionRegistrationOkResponse(ClientSecret: pwd.SecretText, ClientID: app.AppId, TenantID: app.PublisherDomain);
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message, e);
            throw;
        }
    }

    /// <summary>How long to keep secrets around. We expect the ARM template in the managed app to immediately fetch the secret, so one day might be a bit long.</summary>
    private static readonly TimeSpan SecretExpirationPeriod = TimeSpan.FromDays(1);

    public async Task<CreateServicePrincipalInKeyVaultResponse> CreateServicePrincipalInKeyVault(SubscriptionRegistrationRequest subscriptionRegistrationRequest)
    {
        var subscriptionRegistrationOkResponse = await CreateServicePrincipal(subscriptionRegistrationRequest);
        var applicationName = GetApplicationName(subscriptionRegistrationRequest.ManagedBy);

        KeyVaultSecret secret = new(
            name: applicationName,
            value: JsonConvert.SerializeObject(subscriptionRegistrationOkResponse));
        secret.Properties.ExpiresOn = DateTimeOffset.UtcNow.Add(SecretExpirationPeriod);
        secret.Properties.Tags.Add("ManagedBy", subscriptionRegistrationRequest.ManagedBy);
        _ = await _keyVaultSecretClient.SetSecretAsync(secret);

        return new CreateServicePrincipalInKeyVaultResponse(SecretName: applicationName);
    }

    public async Task DeleteServicePrincipalSecret(string managedBy)
    {
        if (!string.IsNullOrEmpty(managedBy) && managedBy.StartsWith("/subscriptions/"))
        {
            var applicationName = GetApplicationName(managedBy);
            var r = await _keyVaultSecretClient.StartDeleteSecretAsync(name: applicationName);
            _ = await r.WaitForCompletionAsync();
        }
    }
}