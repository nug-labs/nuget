using System.Reflection;
using System.Text.Json;
using NugLabs.Models;

namespace NugLabs.Storage;

/// <summary>
/// Handles persisted overrides for the bundled NugLabs dataset.
/// </summary>
public sealed class LocalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _storageDirectory;
    private readonly string _storageFilePath;
    private readonly string _rulesFilePath;
    private readonly Assembly _assembly;

    /// <summary>
    /// Creates a new local store.
    /// </summary>
    /// <param name="storageDirectory">Optional directory used for persisted dataset overrides.</param>
    public LocalStore(string? storageDirectory = null)
    {
        _assembly = typeof(LocalStore).Assembly;
        _storageDirectory = storageDirectory ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NugLabs");
        _storageFilePath = Path.Combine(_storageDirectory, "dataset.json");
        _rulesFilePath = Path.Combine(_storageDirectory, "rules.json");
    }

    /// <summary>
    /// Gets the resolved file path used for persisted dataset overrides.
    /// </summary>
    public string StorageFilePath => _storageFilePath;
    public string RulesFilePath => _rulesFilePath;

    /// <summary>
    /// Gets a value indicating whether the most recent persist attempt succeeded.
    /// </summary>
    public bool CanPersist { get; private set; } = true;

    /// <summary>
    /// Loads the initial dataset, preferring a persisted override over the bundled copy.
    /// </summary>
    /// <returns>The best available local dataset.</returns>
    public IReadOnlyList<Strain> LoadInitialData()
    {
        var bundled = LoadBundledData();
        var persisted = TryLoadPersistedData();
        return persisted ?? bundled;
    }

    /// <summary>
    /// Loads the current dataset, preferring persisted data over the bundled copy.
    /// </summary>
    /// <returns>The best available local dataset.</returns>
    public IReadOnlyList<Strain> LoadCurrentData()
    {
        return TryLoadPersistedData() ?? LoadBundledData();
    }

    /// <summary>
    /// Loads the active normalization rules JSON, preferring persisted override over bundled copy.
    /// </summary>
    public string LoadCurrentRulesJson()
    {
        return TryLoadPersistedRulesJson() ?? LoadBundledRulesJson();
    }

    /// <summary>
    /// Persists a dataset override to local storage.
    /// </summary>
    /// <param name="strains">Dataset to persist.</param>
    /// <param name="cancellationToken">Cancellation token for the write operation.</param>
    public async Task PersistAsync(IReadOnlyList<Strain> strains, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_storageDirectory);

            var tempFilePath = Path.Combine(_storageDirectory, $"{Guid.NewGuid():N}.tmp");
            var json = JsonSerializer.Serialize(strains, JsonOptions);
            await File.WriteAllTextAsync(tempFilePath, json, cancellationToken).ConfigureAwait(false);

            if (File.Exists(_storageFilePath))
            {
                File.Delete(_storageFilePath);
            }

            File.Move(tempFilePath, _storageFilePath, overwrite: true);
            CanPersist = true;
        }
        catch
        {
            CanPersist = false;
        }
    }

    /// <summary>
    /// Persists normalization rules override.
    /// </summary>
    public async Task PersistRulesAsync(string rulesJson, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_storageDirectory);
            var tempFilePath = Path.Combine(_storageDirectory, $"{Guid.NewGuid():N}.rules.tmp");
            await File.WriteAllTextAsync(tempFilePath, rulesJson, cancellationToken).ConfigureAwait(false);
            if (File.Exists(_rulesFilePath))
            {
                File.Delete(_rulesFilePath);
            }

            File.Move(tempFilePath, _rulesFilePath, overwrite: true);
            CanPersist = true;
        }
        catch
        {
            CanPersist = false;
        }
    }

    public async Task<string?> GetSyncEtagAsync(string artifact, CancellationToken cancellationToken = default)
    {
        var etagPath = Path.Combine(_storageDirectory, $"{artifact}.etag");
        try
        {
            if (!File.Exists(etagPath))
            {
                return null;
            }
            return await File.ReadAllTextAsync(etagPath, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetSyncEtagAsync(string artifact, string? etag, CancellationToken cancellationToken = default)
    {
        var etagPath = Path.Combine(_storageDirectory, $"{artifact}.etag");
        try
        {
            Directory.CreateDirectory(_storageDirectory);
            if (string.IsNullOrWhiteSpace(etag))
            {
                if (File.Exists(etagPath))
                {
                    File.Delete(etagPath);
                }
                return;
            }
            await File.WriteAllTextAsync(etagPath, etag, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best effort only.
        }
    }

    private IReadOnlyList<Strain>? TryLoadPersistedData()
    {
        try
        {
            if (!File.Exists(_storageFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(_storageFilePath);
            return DeserializeAndValidate(json);
        }
        catch
        {
            return null;
        }
    }

    private string? TryLoadPersistedRulesJson()
    {
        try
        {
            if (!File.Exists(_rulesFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(_rulesFilePath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            return json;
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<Strain> LoadBundledData()
    {
        var resourceName = _assembly
            .GetManifestResourceNames()
            .First(name => name.EndsWith("dataset.json", StringComparison.OrdinalIgnoreCase));

        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Bundled NugLabs dataset was not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return DeserializeAndValidate(json);
    }

    private string LoadBundledRulesJson()
    {
        var resourceName = _assembly
            .GetManifestResourceNames()
            .First(name => name.EndsWith("rules.json", StringComparison.OrdinalIgnoreCase));

        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Bundled NugLabs rules were not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Invalid bundled rules json.");
        }
        return json;
    }

    private static IReadOnlyList<Strain> DeserializeAndValidate(string json)
    {
        var strains = JsonSerializer.Deserialize<List<Strain>>(json, JsonOptions);
        if (strains is null || strains.Count == 0 || strains.Any(strain => string.IsNullOrWhiteSpace(strain.Name)))
        {
            throw new InvalidOperationException("Invalid NugLabs dataset.");
        }

        return strains;
    }
}
