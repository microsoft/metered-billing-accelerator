namespace Metering.SharedResourceBroker;

public class ServicePrincipalCreatorSettings
{
    internal static class Names
    {
        internal static readonly string SectionName = "ServicePrincipalCreatorSettings";
        internal static readonly string KeyVaultName = nameof(KeyVaultName);
        internal static readonly string AzureADManagedIdentityClientId = "AzureADManagedIdentityClientId";
    }

    /// <summary>
    /// The object ID of the Azure Active Directory group where service principals should be added to.
    /// </summary>
    public Guid SharedResourcesGroup { get; set; }

    /// <summary>
    /// The name of the KeyVault where the BootstrapSecret can be found.
    /// </summary>
    public string KeyVaultName { get; set; }

    public string AzureADManagedIdentityClientId { get; set; }

    public string GeneratedServicePrincipalPrefix { get; set; }
}
