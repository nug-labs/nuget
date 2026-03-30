namespace NugLabs.Options;

/// <summary>
/// Configuration for a <see cref="NugLabsClient"/> instance.
/// </summary>
public sealed class NugLabsClientOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the in-memory cache is enabled.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool CacheInMemory { get; set; } = true;

    /// <summary>
    /// Gets or sets the directory used for persisted dataset overrides.
    /// Defaults to the local application data folder.
    /// </summary>
    public string? StorageDirectory { get; set; }

    /// <summary>
    /// Gets or sets the background sync interval.
    /// Defaults to 12 hours.
    /// </summary>
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Gets or sets the optional HTTP client used for background sync and manual resync.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Rust WASM core is used for lookups/search.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool UseWasm { get; set; } = true;

    /// <summary>
    /// Optional explicit path to <c>nuglabs_core.wasm</c>. When omitted, common package/runtime
    /// locations are searched automatically.
    /// </summary>
    public string? WasmPath { get; set; }
}
