using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace AssetPro.Tests.Infrastructure;

/// <summary>Boots the real API against a throw-away SQLite file seeded with the demo data.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@assetpro.local";
    public const string AdminPassword = "Admin@123";
    public const string UserEmail = "muhammad.ali@assetpro.local";
    public const string OtherUserEmail = "fatima.noor@assetpro.local";
    public const string ApproverEmail = "daniel.carter@assetpro.local";
    public const string UserPassword = "User@1234";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"assetpro-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath}");
        builder.UseSetting("Jwt:Key", "test-signing-key-that-is-long-enough-0123456789");
        builder.UseSetting("Seed:DemoData", "true");
        // Tests assert against the fixed demo scenario; the simulated day-to-day history would only slow them down.
        builder.UseSetting("Seed:HistoryDays", "0");
        builder.UseSetting("RateLimit:LoginPerMinute", "10000");
    }

    public async Task<HttpClient> ClientForAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return client;
    }

    public Task<HttpClient> AdminAsync() => ClientForAsync(AdminEmail, AdminPassword);

    public Task<HttpClient> UserAsync(string email = UserEmail) => ClientForAsync(email, UserPassword);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
    }
}

public static class HttpExtensions
{
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"GET {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.ReadJsonAsync();
    }

    public static async Task<JsonElement> PostOkAsync(this HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, ApiFactory.Json);
        Assert.True(response.IsSuccessStatusCode, $"POST {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.ReadJsonAsync();
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, ApiFactory.Json);

    /// <summary>Creates a fresh in-stock laptop so tests never depend on each other's data.</summary>
    public static async Task<JsonElement> CreateAssetAsync(this HttpClient admin, string? serial = null, string? tag = null)
    {
        return await admin.PostOkAsync("/api/assets", new
        {
            name = "Test Laptop",
            assetTypeId = 1,
            brandId = 1,
            model = "Test Model " + Guid.NewGuid().ToString("N")[..6],
            serialNumber = serial,
            assetTag = tag,
            condition = "New"
        });
    }

    public static int Id(this JsonElement element) => element.GetProperty("id").GetInt32();

    public static string Str(this JsonElement element, string property) => element.GetProperty(property).GetString()!;
}
