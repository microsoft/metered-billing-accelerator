﻿namespace ManagedWebhook.Definitions;

using Newtonsoft.Json;

/// <summary>
/// The application properties.
/// </summary>
public class ApplicationProperties
{
    /// <summary>
    /// Gets or sets the managed by.
    /// </summary>
    [JsonProperty(Required = Required.Default)]
    public BillingDetailsDefinition BillingDetails { get; set; }
}
