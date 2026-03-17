# NugLabs .NET SDK

Local-first SDK for `https://strains.nuglabs.co`.

## Design

- Ships with a bundled `NugLabs/dataset.json`
- Loads bundled data on startup
- Uses persisted local data if a newer synced copy exists
- Performs all reads and searches against local data only
- Auto-syncs from the API every 12 hours
- Supports manual `ForceResyncAsync()`
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
await client.DisposeAsync();
```

```csharp
using NugLabs;
using NugLabs.Options;

var client = new NugLabsClient(new NugLabsClientOptions
{
    ApiBaseUrl = "https://strains.nuglabs.co",
    CacheInMemory = true,
    StorageDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NugLabs"),
    SyncInterval = TimeSpan.FromHours(12),
    HttpClient = new HttpClient()
});
```

## Constructor Options

- `ApiBaseUrl`: base URL used for sync requests
- `CacheInMemory`: enables the in-memory read cache
- `StorageDirectory`: local persistence directory
- `SyncInterval`: background sync interval
- `HttpClient`: custom HTTP client for sync

## Return Shapes

- `GetStrainAsync(name)`: returns `Strain?`
- `GetAllStrainsAsync()`: returns `IReadOnlyList<Strain>`
- `SearchStrains(query)`: returns `IReadOnlyList<Strain>`
- `ForceResyncAsync()`: returns `NugLabsSyncResult`

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
- Sync failures keep the last good local dataset
