using NugLabs.Models;
using NugLabs.Options;
using NugLabs.Storage;
using NugLabs.Sync;
using NugLabs.Wasm;

namespace NugLabs;

/// <summary>
/// Local-first client for querying and syncing the NugLabs strain dataset.
/// </summary>
public sealed class NugLabsClient : IDisposable, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly LocalStore _store;
    private readonly SyncService _syncService;
    private readonly object _wasmGate = new();
    private readonly WasmBridge _wasm;

    /// <summary>
    /// Creates a new client instance from a configuration object.
    /// </summary>
    /// <param name="options">
    /// Optional runtime configuration.
    /// <list type="bullet">
    /// <item><description><c>StorageDirectory</c>: optional local persistence directory</description></item>
    /// <item><description><c>SyncInterval</c>: defaults to 12 hours</description></item>
    /// <item><description><c>HttpClient</c>: optional custom HTTP client for sync</description></item>
    /// <item><description><c>WasmPath</c>: optional explicit path to <c>nuglabs_core.wasm</c></description></item>
    /// </list>
    /// </param>
    public NugLabsClient(NugLabsClientOptions options)
        : this(
            httpClient: options.HttpClient,
            syncInterval: options.SyncInterval,
            storageDirectory: options.StorageDirectory,
            wasmPath: options.WasmPath)
    {
    }

    /// <summary>
    /// Creates a new client instance.
    /// </summary>
    /// <param name="httpClient">Optional HTTP client used for background sync and manual resync.</param>
    /// <param name="syncInterval">Background sync interval. Defaults to 12 hours.</param>
    /// <param name="storageDirectory">Optional directory used for persisted dataset overrides.</param>
    /// <param name="wasmPath">Optional explicit path to <c>nuglabs_core.wasm</c>.</param>
    public NugLabsClient(
        HttpClient? httpClient = null,
        TimeSpan? syncInterval = null,
        string? storageDirectory = null,
        string? wasmPath = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _store = new LocalStore(storageDirectory);
        var dataset = _store.LoadInitialData();
        var rulesJson = _store.LoadCurrentRulesJson();

        _wasm = WasmBridge.Create(wasmPath);
        _wasm.LoadRules(rulesJson);
        _wasm.LoadDataset(System.Text.Json.JsonSerializer.Serialize(dataset));
        _syncService = new SyncService(
            _httpClient,
            _store,
            UpdateDataset,
            UpdateRules,
            syncInterval ?? TimeSpan.FromHours(12));

        _syncService.Start();
    }

    /// <summary>
    /// Returns a single strain by exact case-insensitive match on <see cref="Strain.Name"/> or aliases.
    /// </summary>
    /// <param name="name">Strain name or alias to resolve.</param>
    /// <param name="cancellationToken">Cancellation token for the lookup.</param>
    /// <returns>
    /// A <see cref="Strain"/> object containing fields like <see cref="Strain.Name"/>,
    /// optional <see cref="Strain.Id"/>, optional aliases, and any additional dataset fields,
    /// or <c>null</c> when nothing matches.
    /// </returns>
    public Task<Strain?> GetStrainAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_wasmGate)
        {
            return Task.FromResult(_wasm.GetStrain(name));
        }
    }

    /// <summary>
    /// Returns the full locally available strain dataset.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the lookup.</param>
    /// <returns>
    /// A list of <see cref="Strain"/> objects from the local dataset. Each item typically includes
    /// <see cref="Strain.Name"/>, optional <see cref="Strain.Id"/>, aliases, and any additional
    /// NugLabs strain fields.
    /// </returns>
    public Task<IReadOnlyList<Strain>> GetAllStrainsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_wasmGate)
        {
            return Task.FromResult(_wasm.GetAllStrains());
        }
    }

    /// <summary>
    /// Performs a case-insensitive partial search against strain names and aliases.
    /// </summary>
    /// <param name="query">Partial query to search for.</param>
    /// <returns>
    /// A list of matching <see cref="Strain"/> objects. Each result typically includes
    /// <see cref="Strain.Name"/>, optional <see cref="Strain.Id"/>, aliases, and any additional
    /// NugLabs strain fields.
    /// </returns>
    public IReadOnlyList<Strain> SearchStrains(string query)
    {
        lock (_wasmGate)
        {
            return _wasm.SearchStrains(query);
        }
    }

    /// <summary>
    /// Fetches the latest dataset from the NugLabs API and replaces the local copy.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the sync operation.</param>
    /// <returns>Metadata about the applied sync.</returns>
    public async Task<NugLabsSyncResult> ForceResyncAsync(CancellationToken cancellationToken = default)
    {
        return await _syncService.ForceResyncAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches and applies only the dataset artifact from remote API.
    /// </summary>
    public async Task<NugLabsArtifactSyncResult> ForceResyncDatasetAsync(CancellationToken cancellationToken = default)
    {
        return await _syncService.ForceResyncDatasetAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches and applies only the normalization rules artifact from remote API.
    /// </summary>
    public async Task<NugLabsArtifactSyncResult> ForceResyncRulesAsync(CancellationToken cancellationToken = default)
    {
        return await _syncService.ForceResyncRulesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the client and stops background sync.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously disposes the client and stops background sync.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _syncService.DisposeAsync().ConfigureAwait(false);
        _wasm.Dispose();

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private void UpdateDataset(IReadOnlyList<Strain> strains)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(strains);
        lock (_wasmGate)
        {
            _wasm.LoadDataset(json);
        }
    }

    private void UpdateRules(string rulesJson)
    {
        lock (_wasmGate)
        {
            _wasm.LoadRules(rulesJson);
        }
    }
}
