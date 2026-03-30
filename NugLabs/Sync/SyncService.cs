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
    private readonly Action<string>? _onRulesUpdated;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task? _backgroundTask;

    /// <summary>
    /// Creates a new sync service.
    /// </summary>
    /// <param name="httpClient">HTTP client used for API requests.</param>
    /// <param name="store">Local store that receives persisted overrides.</param>
    /// <param name="onDatasetUpdated">Callback invoked after a successful sync.</param>
    /// <param name="interval">Interval used for background sync.</param>
    public SyncService(
        HttpClient httpClient,
        LocalStore store,
        Action<IReadOnlyList<Strain>> onDatasetUpdated,
        Action<string>? onRulesUpdated,
        TimeSpan interval)
    {
        _httpClient = httpClient;
        _store = store;
        _onDatasetUpdated = onDatasetUpdated;
        _onRulesUpdated = onRulesUpdated;
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
    /// Fetches dataset + rules from the API and stores locally.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the sync operation.</param>
    /// <returns>Metadata about the applied sync.</returns>
    public async Task<NugLabsSyncResult> ForceResyncAsync(CancellationToken cancellationToken = default)
    {
        var dataset = await ForceResyncDatasetAsync(cancellationToken).ConfigureAwait(false);
        var rules = await ForceResyncRulesAsync(cancellationToken).ConfigureAwait(false);
        return new NugLabsSyncResult { Dataset = dataset, Rules = rules };
    }

    /// <summary>
    /// Fetches only the dataset artifact from the API and stores it locally.
    /// </summary>
    public async Task<NugLabsArtifactSyncResult> ForceResyncDatasetAsync(CancellationToken cancellationToken = default)
    {
        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
        var etag = await _store.GetSyncEtagAsync("dataset", linkedTokenSource.Token).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, global::NugLabs.NugLabsApi.StrainsDatasetUrl);
        if (!string.IsNullOrWhiteSpace(etag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }
        var response = await _httpClient.SendAsync(request, linkedTokenSource.Token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            return new NugLabsArtifactSyncResult
            {
                Artifact = "dataset",
                Changed = false,
                Source = "not-modified",
                ETag = etag,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(linkedTokenSource.Token).ConfigureAwait(false);
        var dataset = JsonSerializer.Deserialize<List<Strain>>(json, JsonOptions);
        if (dataset is null || dataset.Count == 0 || dataset.Any(strain => string.IsNullOrWhiteSpace(strain.Name)))
        {
            throw new InvalidOperationException("NugLabs sync returned an invalid dataset.");
        }
        var nextEtag = response.Headers.ETag?.Tag;
        await _store.PersistAsync(dataset, linkedTokenSource.Token).ConfigureAwait(false);
        await _store.SetSyncEtagAsync("dataset", nextEtag, linkedTokenSource.Token).ConfigureAwait(false);
        _onDatasetUpdated(dataset);
        return new NugLabsArtifactSyncResult
        {
            Artifact = "dataset",
            Changed = true,
            Count = dataset.Count,
            Source = "remote",
            ETag = nextEtag,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Fetches only the rules artifact from the API and stores it locally.
    /// </summary>
    public async Task<NugLabsArtifactSyncResult> ForceResyncRulesAsync(CancellationToken cancellationToken = default)
    {
        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
        var etag = await _store.GetSyncEtagAsync("rules", linkedTokenSource.Token).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, global::NugLabs.NugLabsApi.RulesUrl);
        if (!string.IsNullOrWhiteSpace(etag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }
        var response = await _httpClient.SendAsync(request, linkedTokenSource.Token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            return new NugLabsArtifactSyncResult
            {
                Artifact = "rules",
                Changed = false,
                Source = "not-modified",
                ETag = etag,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Backward compatibility: some deployments may not expose a dedicated rules endpoint yet.
            return new NugLabsArtifactSyncResult
            {
                Artifact = "rules",
                Changed = false,
                Source = "not-modified",
                ETag = etag,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(linkedTokenSource.Token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("NugLabs sync returned invalid rules.");
        }
        var nextEtag = response.Headers.ETag?.Tag;
        await _store.PersistRulesAsync(json, linkedTokenSource.Token).ConfigureAwait(false);
        await _store.SetSyncEtagAsync("rules", nextEtag, linkedTokenSource.Token).ConfigureAwait(false);
        _onRulesUpdated?.Invoke(json);
        return new NugLabsArtifactSyncResult
        {
            Artifact = "rules",
            Changed = true,
            Source = "remote",
            ETag = nextEtag,
            UpdatedAt = DateTimeOffset.UtcNow
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
