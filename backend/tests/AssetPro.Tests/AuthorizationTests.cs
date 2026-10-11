using System.Net;
using System.Net.Http.Json;
using AssetPro.Tests.Infrastructure;

namespace AssetPro.Tests;

/// <summary>Broken access control / IDOR checks for a standard user (Muhammad, employee #2).</summary>
public sealed class AuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/api/assets")]
    [InlineData("/api/employees")]
    [InlineData("/api/inventory")]
    [InlineData("/api/history")]
    [InlineData("/api/dashboard")]
    [InlineData("/api/reports/assets")]
    [InlineData("/api/activity-logs")]
    [InlineData("/api/activity-logs/application")]
    [InlineData("/api/users")]
    public async Task Standard_user_is_forbidden_from_admin_screens(string url)
    {
        var user = await factory.UserAsync();
        var response = await user.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // Framework 403s still use the uniform error body.
        Assert.Equal(403, (await response.ReadJsonAsync()).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Standard_user_cannot_perform_admin_operations()
    {
        var user = await factory.UserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostJsonAsync("/api/assets", new { name = "Hack", assetTypeId = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostJsonAsync("/api/assets/1/assign", new { employeeId = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostJsonAsync("/api/assets/1/scrap", new { reason = "because" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostJsonAsync("/api/inventory/stock-in", new { assetTypeId = 1, name = "x", quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostJsonAsync("/api/lookups/departments", new { name = "Hackers" })).StatusCode);
    }

    [Fact]
    public async Task Standard_user_can_only_open_own_employee_profile()
    {
        var user = await factory.UserAsync();
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/employees/2")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync("/api/employees/3")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/me/profile")).StatusCode);
    }

    [Fact]
    public async Task Standard_user_can_only_open_assets_they_hold()
    {
        var user = await factory.UserAsync();
        var profile = await user.GetJsonAsync("/api/me/profile");
        var mine = profile.GetProperty("assets").EnumerateArray().First().Id();
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/assets/{mine}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/assets/{mine}/history")).StatusCode);

        // LAP-00002 belongs to Fatima.
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync("/api/assets/2")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync("/api/assets/2/history")).StatusCode);
    }

    [Fact]
    public async Task Standard_user_only_sees_own_requests()
    {
        var user = await factory.UserAsync();
        var list = await user.GetJsonAsync("/api/requests?pageSize=100");
        Assert.All(list.GetProperty("items").EnumerateArray(), r => Assert.Equal(2, r.GetProperty("requesterEmployeeId").GetInt32()));

        var admin = await factory.AdminAsync();
        var all = await admin.GetJsonAsync("/api/requests?pageSize=100");
        var someoneElses = all.GetProperty("items").EnumerateArray().First(r => r.GetProperty("requesterEmployeeId").GetInt32() != 2).Id();
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/requests/{someoneElses}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.PostJsonAsync($"/api/requests/{someoneElses}/check", new { })).StatusCode);
    }

    [Fact]
    public async Task Standard_user_cannot_raise_request_for_someone_elses_asset()
    {
        var user = await factory.UserAsync();
        var response = await user.PostJsonAsync("/api/requests", new { type = "Repair", assetId = 2, comments = "Not my laptop" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Granted_permission_unlocks_endpoint()
    {
        // Daniel is a standard user with Requests.Approve + Dashboard.View granted in the seed.
        var approver = await factory.UserAsync(ApiFactory.ApproverEmail);
        Assert.Equal(HttpStatusCode.OK, (await approver.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.GetAsync("/api/assets")).StatusCode);
    }
}

/// <summary>A standard user holding Users.Manage must not be able to escalate privileges.</summary>
public sealed class PrivilegeEscalationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Delegated_user_manager_cannot_escalate()
    {
        var admin = await factory.AdminAsync();
        var users = await admin.GetJsonAsync("/api/users?pageSize=50");
        var fatima = users.GetProperty("items").EnumerateArray().First(u => u.Str("email") == ApiFactory.OtherUserEmail).Id();
        var muhammad = users.GetProperty("items").EnumerateArray().First(u => u.Str("email") == ApiFactory.UserEmail).Id();
        (await admin.PutAsJsonAsync($"/api/users/{fatima}", new { role = "User", isActive = true, permissions = new[] { "Users.Manage" } })).EnsureSuccessStatusCode();

        var manager = await factory.UserAsync(ApiFactory.OtherUserEmail);
        // Cannot grant themselves more, cannot promote anyone to admin, cannot touch the admin.
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync($"/api/users/{fatima}", new { role = "User", isActive = true, permissions = new[] { "Users.Manage", "Assets.Scrap" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync($"/api/users/{muhammad}", new { role = "Admin", isActive = true, permissions = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostJsonAsync("/api/users/1/reset-password", new { password = "Hijack1234" })).StatusCode);
        // Managing an ordinary user is still allowed.
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync($"/api/users/{muhammad}", new { role = "User", isActive = true, permissions = new[] { "Dashboard.View" } })).StatusCode);
    }
}
