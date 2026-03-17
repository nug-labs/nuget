using NugLabs.Models;
using NugLabs.Options;
using NugLabs.Search;
using NugLabs.Storage;
using NugLabs.Sync;

namespace NugLabs;

/// <summary>
/// Local-first client for querying and syncing the NugLabs strain dataset.
/// </summary>
public sealed class NugLabsClient : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly bool _cacheInMemory;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly LocalStore _store;
    private readonly SyncService _syncService;
    private List<Strain> _memoryCache;

    /// <summary>
    /// Creates a new client instance from a configuration object.
    /// </summary>
    /// <param name="options">
    /// Optional runtime configuration.
    /// <list type="bullet">
    /// <item><description><c>ApiBaseUrl</c>: defaults to <c>https://strains.nuglabs.co</c></description></item>
    /// <item><description><c>CacheInMemory</c>: defaults to <c>true</c></description></item>
    /// <item><description><c>StorageDirectory</c>: optional local persistence directory</description></item>
    /// <item><description><c>SyncInterval</c>: defaults to 12 hours</description></item>
    /// <item><description><c>HttpClient</c>: optional custom HTTP client for sync</description></item>
    /// </list>
    /// </param>
    public NugLabsClient(NugLabsClientOptions options)
        : this(
            httpClient: options.HttpClient,
            cacheInMemory: options.CacheInMemory,
            apiBaseUrl: options.ApiBaseUrl,
            syncInterval: options.SyncInterval,
            storageDirectory: options.StorageDirectory)
    {
    }

    /// <summary>
    /// Creates a new client instance.
    /// </summary>
    /// <param name="httpClient">Optional HTTP client used for background sync and manual resync.</param>
    /// <param name="cacheInMemory">Enables the in-memory read cache when <c>true</c>.</param>
    /// <param name="apiBaseUrl">Base API URL used for sync requests.</param>
    /// <param name="syncInterval">Background sync interval. Defaults to 12 hours.</param>
    /// <param name="storageDirectory">Optional directory used for persisted dataset overrides.</param>
    public NugLabsClient(
        HttpClient? httpClient = null,
        bool cacheInMemory = true,
        string apiBaseUrl = "https://strains.nuglabs.co",
        TimeSpan? syncInterval = null,
        string? storageDirectory = null)
    {
        _cacheInMemory = cacheInMemory;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _store = new LocalStore(storageDirectory);
        _memoryCache = _store.LoadInitialData().ToList();
        _syncService = new SyncService(
            _httpClient,
            _store,
            UpdateDataset,
            apiBaseUrl,
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
        return Task.FromResult(StrainSearch.GetStrain(GetDatasetSnapshot(), name));
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
        return Task.FromResult(GetDatasetSnapshot());
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
        return StrainSearch.SearchStrains(GetDatasetSnapshot(), query);
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

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private IReadOnlyList<Strain> GetDatasetSnapshot()
    {
        if (_cacheInMemory)
        {
            lock (_gate)
            {
                return _memoryCache.ToList();
            }
        }

        return _store.LoadCurrentData();
    }

    private void UpdateDataset(IReadOnlyList<Strain> strains)
    {
        if (!_cacheInMemory)
        {
            return;
        }

        lock (_gate)
        {
            _memoryCache = strains.ToList();
        }
    }
}
