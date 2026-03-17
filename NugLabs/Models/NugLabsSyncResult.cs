namespace NugLabs.Models;

/// <summary>
/// Result returned after a successful remote sync.
/// </summary>
public sealed class NugLabsSyncResult
{
    /// <summary>
    /// Gets or sets the UTC timestamp for when the dataset was refreshed.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Gets or sets the number of strain records loaded from the remote API.
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// Gets or sets the source of the applied dataset.
    /// </summary>
    public string Source { get; init; } = "remote";
}
