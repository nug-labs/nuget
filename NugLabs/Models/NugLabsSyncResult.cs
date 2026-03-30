namespace NugLabs.Models;

public sealed class NugLabsArtifactSyncResult
{
    /// <summary>Artifact kind: <c>dataset</c> or <c>rules</c>.</summary>
    public string Artifact { get; init; } = string.Empty;
    /// <summary>True when a changed payload was downloaded (HTTP 200).</summary>
    public bool Changed { get; init; }
    /// <summary>Dataset item count (when <c>Artifact</c> is dataset).</summary>
    public int? Count { get; init; }
    /// <summary>ETag returned by the server when present.</summary>
    public string? ETag { get; init; }
    /// <summary><c>remote</c> for HTTP 200, <c>not-modified</c> for 304.</summary>
    public string Source { get; init; } = "remote";
    /// <summary>UTC timestamp for this artifact sync.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Result returned by dataset+rules sync.</summary>
public sealed class NugLabsSyncResult
{
    public NugLabsArtifactSyncResult Dataset { get; init; } = new() { Artifact = "dataset" };
    public NugLabsArtifactSyncResult Rules { get; init; } = new() { Artifact = "rules" };
}
