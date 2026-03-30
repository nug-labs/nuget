# NugLabs .NET SDK

Local-first SDK for `https://strains.nuglabs.co`.

Current NuGet package version: `1.3.1`.

## Design

- Ships with a bundled `NugLabs/dataset.json`
- Loads bundled data on startup
- Uses persisted local data if a newer synced copy exists
- Performs all reads and searches against local data only
- Auto-syncs from the API every 12 hours
- Supports manual `ForceResyncAsync()` (`dataset` + `rules`)
- Supports targeted `ForceResyncDatasetAsync()` and `ForceResyncRulesAsync()`
- Uses ETag conditional requests (`If-None-Match`) for sync efficiency
- Falls back to memory-only behavior if local writes fail

## Install

```bash
dotnet add package NugLabs
```

## Usage

```csharp
using NugLabs;

var client = new NugLabsClient();

var blueDream = await client.GetStrainAsync("Blue Dream");
var allStrains = await client.GetAllStrainsAsync();
var matches = client.SearchStrains("dream");

var sync = await client.ForceResyncAsync();
var datasetOnly = await client.ForceResyncDatasetAsync();
var rulesOnly = await client.ForceResyncRulesAsync();
await client.DisposeAsync();
```

```csharp
using NugLabs;
using NugLabs.Options;

var client = new NugLabsClient(new NugLabsClientOptions
{
    StorageDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NugLabs"),
    SyncInterval = TimeSpan.FromHours(12),
    HttpClient = new HttpClient()
});
```

Sync uses canonical URLs from `NugLabsApi.StrainsDatasetUrl` and `NugLabsApi.RulesUrl` (aligned with Rust `nuglabs_core::strains_dataset_url()` / `nuglabs_core::rules_url()`).

## Constructor Options

- `StorageDirectory`: local persistence directory
- `SyncInterval`: background sync interval
- `HttpClient`: custom HTTP client for sync
- `WasmPath`: optional explicit path to `nuglabs_core.wasm`

## Return Shapes

- `GetStrainAsync(name)`: returns `Strain?`
- `GetAllStrainsAsync()`: returns `IReadOnlyList<Strain>`
- `SearchStrains(query)`: returns `IReadOnlyList<Strain>`
- `ForceResyncAsync()`: returns `NugLabsSyncResult`
- `ForceResyncDatasetAsync()`: returns `NugLabsArtifactSyncResult`
- `ForceResyncRulesAsync()`: returns `NugLabsArtifactSyncResult`

Typical `Strain` fields include:

- `Id`
- `Name`
- `Akas`
- `Type`
- `Thc`
- `Description`
- plus any additional dataset fields bundled with NugLabs

## Behavior

- `GetStrainAsync(name)` does case-insensitive exact matching against `Name` and `Akas`
- `SearchStrains(query)` does case-insensitive partial matching against `Name` and `Akas`
- `GetAllStrainsAsync()` returns the full locally loaded dataset
- Reads never call the API directly
- Sync failures keep the last good local artifacts
- Rules endpoint `404` is treated as `not-modified` for backward-compatible deployments
