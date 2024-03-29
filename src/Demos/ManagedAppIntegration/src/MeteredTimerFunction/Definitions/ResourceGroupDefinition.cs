﻿namespace ManagedWebhook.Definitions;

using Newtonsoft.Json;

/// <summary>
/// The resource group definition
/// </summary>
public class ResourceGroupDefinition
{
    /// <summary>
    /// Gets or sets the managed by.
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    public string ManagedBy { get; set; }
}
