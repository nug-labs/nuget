namespace NugLabs;

/// <summary>
/// Canonical NugLabs HTTP API endpoints. Keep in sync with <c>nuglabs_core::constants</c> (<c>app/sdk/core/src/constants.rs</c>).
/// </summary>
public static class NugLabsApi
{
    /// <summary>Production API origin.</summary>
    public const string Origin = "https://strains.nuglabs.co";

    /// <summary>
    /// Full URL for the JSON strain list used by <see cref="Sync.SyncService"/>.
    /// </summary>
    public const string StrainsDatasetUrl = Origin + "/api/v1/strains";

    /// <summary>
    /// Full URL for normalization rules JSON used by <see cref="Sync.SyncService"/>.
    /// </summary>
    public const string RulesUrl = Origin + "/api/v1/strains/rules";
}
