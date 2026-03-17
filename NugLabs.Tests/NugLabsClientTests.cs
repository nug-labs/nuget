using System.Net;
using System.Text;
using NugLabs.Models;
using NugLabs.Options;
using Xunit;

namespace NugLabs.Tests;

public sealed class NugLabsClientTests
{
    [Fact]
    public async Task GetStrainAsync_MatchesNameCaseInsensitively()
    {
        await using var client = new NugLabsClient(
            cacheInMemory: true,
            syncInterval: TimeSpan.FromDays(1),
            storageDirectory: CreateStorageDirectory());

        var strain = await client.GetStrainAsync("blue dream");

        Assert.NotNull(strain);
        Assert.Equal("Blue Dream", strain!.Name);
    }

    [Fact]
    public async Task SearchStrains_FindsPartialMatches()
    {
        await using var client = new NugLabsClient(
            cacheInMemory: true,
            syncInterval: TimeSpan.FromDays(1),
            storageDirectory: CreateStorageDirectory());

        var results = client.SearchStrains("dream");

        Assert.Contains(results, strain => strain.Name == "Blue Dream");
    }

    [Fact]
    public async Task ForceResyncAsync_UpdatesMemoryAndDisk()
    {
        var storageDirectory = CreateStorageDirectory();
        using var httpClient = new HttpClient(new StubHttpMessageHandler("""
            [
              {
                "name": "Test Strain",
                "akas": ["TS"]
              }
            ]
            """));

        await using var client = new NugLabsClient(new NugLabsClientOptions
        {
            HttpClient = httpClient,
            CacheInMemory = true,
            SyncInterval = TimeSpan.FromDays(1),
            StorageDirectory = storageDirectory
        });

        var result = await client.ForceResyncAsync();
        Assert.Equal(1, result.Count);
        Assert.Equal("remote", result.Source);

        var byAlias = await client.GetStrainAsync("ts");
        Assert.NotNull(byAlias);
        Assert.Equal("Test Strain", byAlias!.Name);

        var persistedJson = await File.ReadAllTextAsync(Path.Combine(storageDirectory, "dataset.json"));
        Assert.Contains("Test Strain", persistedJson);
    }

    [Fact]
    public async Task ForceResyncAsync_KeepsExistingDatasetWhenSyncFails()
    {
        var storageDirectory = CreateStorageDirectory();
        using var httpClient = new HttpClient(new FailingHttpMessageHandler());

        await using var client = new NugLabsClient(
            httpClient: httpClient,
            cacheInMemory: true,
            syncInterval: TimeSpan.FromDays(1),
            storageDirectory: storageDirectory);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ForceResyncAsync());

        var existing = await client.GetStrainAsync("Blue Dream");
        Assert.NotNull(existing);
        Assert.Equal("Blue Dream", existing!.Name);
    }

    [Fact]
    public async Task Constructor_WithOptions_LoadsDataAndSearches()
    {
        await using var client = new NugLabsClient(new NugLabsClientOptions
        {
            CacheInMemory = true,
            SyncInterval = TimeSpan.FromDays(1),
            StorageDirectory = CreateStorageDirectory()
        });

        var strain = await client.GetStrainAsync("mimosa");
        Assert.NotNull(strain);
        Assert.Equal("Mimosa", strain!.Name);

        var results = client.SearchStrains("dream");
        Assert.Contains(results, item => item.Name == "Blue Dream");
    }

    private static string CreateStorageDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "nuglabs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubHttpMessageHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }

    private sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("boom");
        }
    }
}
