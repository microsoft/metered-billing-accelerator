namespace Metering.SharedResourceBroker;

using System.Text.Json;
using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProvisioningState { Accepted, Succeeded, Deleting, Deleted, }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventType { PUT, PATCH, DELETE, }

public record Detail(string Code, string Message);

public record Error(string Code, string Message, Detail[] Details);

public record Billingdetails(string ResourceUsageId);

public record Plan(string Publisher, string Product, string Name, string Version);

public record CommonNotificationInformation(EventType EventType, ProvisioningState ProvisioningState, string ApplicationId, DateTime EventTime);

public record AzureMarketplaceApplicationNotification(EventType EventType, ProvisioningState ProvisioningState, string ApplicationId, DateTime EventTime, Billingdetails BillingDetails, Plan Plan, Error Error);

public record ServiceCatalogApplicationNotification(EventType EventType, ProvisioningState ProvisioningState, string ApplicationId, DateTime EventTime, string ApplicationDefinitionId, Error Error);

public static class CamelCaseJsonExtensions
{
    private static readonly JsonSerializerOptions options = new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static T DeserializeJson<T>(this string json) => JsonSerializer.Deserialize<T>(json, options);

    public static string SerializeJson<T>(this T t) => JsonSerializer.Serialize(t, options);
}
