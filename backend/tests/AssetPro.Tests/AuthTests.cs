using System.Net;
using System.Net.Http.Json;
using AssetPro.Tests.Infrastructure;

namespace AssetPro.Tests;

public sealed class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Login_with_valid_credentials_returns_token_and_permissions()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.False(string.IsNullOrEmpty(body.Str("token")));
        Assert.Equal("Admin", body.GetProperty("user").Str("role"));
        Assert.Contains("Users.Manage", body.GetProperty("user").GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_return_the_same_message()
    {
        var client = factory.CreateClient();
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = ApiFactory.AdminEmail, password = "nope-nope-1" });
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = "ghost@assetpro.local", password = "nope-nope-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal((await wrong.ReadJsonAsync()).Str("message"), (await unknown.ReadJsonAsync()).Str("message"));
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures()
    {
        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/login", new { email = ApiFactory.OtherUserEmail, password = "wrong-pass-1" });

        var locked = await client.PostAsJsonAsync("/api/auth/login", new { email = ApiFactory.OtherUserEmail, password = ApiFactory.UserPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Contains("Too many failed attempts", (await locked.ReadJsonAsync()).Str("message"));
    }

    [Fact]
    public async Task Invalid_email_is_rejected_by_validation()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "not-an-email", password = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task Requests_without_token_are_unauthorized()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/assets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var client = await factory.AdminAsync();
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token[..^4] + "abcd");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/assets")).StatusCode);
    }

    [Fact]
    public async Task Token_is_revoked_when_permissions_change()
    {
        var admin = await factory.AdminAsync();
        var approver = await factory.UserAsync(ApiFactory.ApproverEmail);
        Assert.Equal(HttpStatusCode.OK, (await approver.GetAsync("/api/dashboard")).StatusCode);

        var users = await admin.GetJsonAsync("/api/users?pageSize=50");
        var daniel = users.GetProperty("items").EnumerateArray().First(u => u.Str("email") == ApiFactory.ApproverEmail);
        var update = await admin.PutAsJsonAsync($"/api/users/{daniel.Id()}", new { role = "User", isActive = true, permissions = new[] { "Requests.Approve" } });
        update.EnsureSuccessStatusCode();

        // Old token carries a stale security stamp.
        Assert.Equal(HttpStatusCode.Unauthorized, (await approver.GetAsync("/api/dashboard")).StatusCode);
        // After signing in again the reduced permission set applies.
        var fresh = await factory.UserAsync(ApiFactory.ApproverEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await fresh.GetAsync("/api/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_demote_themselves()
    {
        var admin = await factory.AdminAsync();
        var response = await admin.PutAsJsonAsync("/api/users/1", new { role = "User", isActive = true, permissions = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
