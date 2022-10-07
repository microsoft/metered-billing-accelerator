namespace Metering.SharedResourceBroker;

public record SubscriptionRegistrationRequest(string ManagedBy);

public record CreateServicePrincipalInKeyVaultResponse(string SecretName);

internal record SubscriptionRegistrationOkResponse(string ClientID, string ClientSecret, string TenantID);

public record SubscriptionRegistrationFailedResponse(string Message);

internal static class DTOValidation
{
    public static void EnsureValid(this SubscriptionRegistrationRequest subscriptionRegistrationRequest)
    {
        if (string.IsNullOrEmpty(subscriptionRegistrationRequest.ManagedBy))
        {
            throw new ArgumentNullException(
                paramName: nameof(subscriptionRegistrationRequest),
                message: $"Missing {nameof(SubscriptionRegistrationRequest)}.{nameof(SubscriptionRegistrationRequest.ManagedBy)}");
        }
    }
}