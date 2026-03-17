using System.Text.Json;
using NugLabs.Models;
using NugLabs.Storage;

namespace NugLabs.Sync;

/// <summary>
/// Coordinates background and manual sync operations for the local dataset.
/// </summary>
public sealed class SyncService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly LocalStore _store;
    private readonly Action<IReadOnlyList<Strain>> _onDatasetUpdated;
    private readonly string _apiBaseUrl;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task? _backgroundTask;

    /// <summary>
    /// Creates a new sync service.
    /// </summary>
    /// <param name="httpClient">HTTP client used for API requests.</param>
    /// <param name="store">Local store that receives persisted overrides.</param>
    /// <param name="onDatasetUpdated">Callback invoked after a successful sync.</param>
    /// <param name="apiBaseUrl">Base API URL used for sync requests.</param>
    /// <param name="interval">Interval used for background sync.</param>
    public SyncService(
        HttpClient httpClient,
        LocalStore store,
        Action<IReadOnlyList<Strain>> onDatasetUpdated,
        string apiBaseUrl,
        TimeSpan interval)
    {
        _httpClient = httpClient;
        _store = store;
        _onDatasetUpdated = onDatasetUpdated;
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
        _interval = interval;
    }

    /// <summary>
    /// Starts the background sync loop.
    /// </summary>
    public void Start()
    {
        _backgroundTask ??= Task.Run(RunAsync);
    }

    /// <summary>
    /// Fetches the latest dataset from the API and stores it locally.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the sync operation.</param>
    /// <returns>Metadata about the applied sync.</returns>
    public async Task<NugLabsSyncResult> ForceResyncAsync(CancellationToken cancellationToken = default)
    {
        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _cancellationTokenSource.Token);

        var response = await _httpClient.GetAsync($"{_apiBaseUrl}/api/v1/strains", linkedTokenSource.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(linkedTokenSource.Token).ConfigureAwait(false);
        var dataset = JsonSerializer.Deserialize<List<Strain>>(json, JsonOptions);
        if (dataset is null || dataset.Count == 0 || dataset.Any(strain => string.IsNullOrWhiteSpace(strain.Name)))
        {
            throw new InvalidOperationException("NugLabs sync returned an invalid dataset.");
        }

        await _store.PersistAsync(dataset, linkedTokenSource.Token).ConfigureAwait(false);
        _onDatasetUpdated(dataset);
        return new NugLabsSyncResult
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            Count = dataset.Count,
            Source = "remote"
        };
    }

    /// <summary>
    /// Stops background sync and disposes internal resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _cancellationTokenSource.Cancel();

        if (_backgroundTask is not null)
        {
          await _backgroundTask.ConfigureAwait(false);
        }

        _cancellationTokenSource.Dispose();
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(_cancellationTokenSource.Token).ConfigureAwait(false))
            {
                try
                {
                    await ForceResyncAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Keep the current dataset if sync fails.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
    }
}
