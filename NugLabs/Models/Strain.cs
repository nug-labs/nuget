using System.Text.Json;
using System.Text.Json.Serialization;

namespace NugLabs.Models;

/// <summary>
/// Represents a single strain record from the NugLabs dataset.
/// </summary>
public sealed class Strain
{
    /// <summary>
    /// Gets or sets the optional numeric identifier for the strain.
    /// </summary>
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    /// <summary>
    /// Gets or sets the primary display name for the strain.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional strain type.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the optional THC percentage.
    /// </summary>
    [JsonPropertyName("thc")]
    public double? Thc { get; set; }

    /// <summary>
    /// Gets or sets the optional strain description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets alternate names used for exact and partial matching.
    /// </summary>
    [JsonPropertyName("akas")]
    public List<string> Akas { get; set; } = [];

    /// <summary>
    /// Gets or sets any additional dataset fields not modeled explicitly.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}
