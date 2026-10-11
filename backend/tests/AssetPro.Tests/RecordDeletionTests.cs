using System.Net;
using AssetPro.Tests.Infrastructure;

namespace AssetPro.Tests;

/// <summary>
/// Permanent deletion is administrators-only and refuses anything that would leave the data
/// inconsistent. Each test creates what it deletes, so nothing here depends on the demo scenario.
/// </summary>
public sealed class RecordDeletionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/api/assets/1")]
    [InlineData("/api/employees/3")]
    [InlineData("/api/requests/1")]
    [InlineData("/api/inventory/1")]
    [InlineData("/api/users/1")]
    public async Task Standard_user_cannot_delete_anything(string url)
    {
        var user = await factory.UserAsync();
        var response = await user.DeleteAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_deletes_an_asset_with_its_history()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();
        var id = asset.Id();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/assets/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/assets/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/assets/{id}/history")).StatusCode);
    }

    [Fact]
    public async Task Deleting_an_assigned_asset_frees_the_holder()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();
        await admin.PostOkAsync($"/api/assets/{asset.Id()}/assign", new { employeeId = 3 });

        var before = (await admin.GetJsonAsync("/api/employees/3")).GetProperty("assets").GetArrayLength();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/assets/{asset.Id()}")).StatusCode);

        var after = (await admin.GetJsonAsync("/api/employees/3")).GetProperty("assets").GetArrayLength();
        Assert.Equal(before - 1, after);
    }

    [Fact]
    public async Task Employee_holding_assets_cannot_be_deleted()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();
        await admin.PostOkAsync($"/api/assets/{asset.Id()}/assign", new { employeeId = 3 });

        var response = await admin.DeleteAsync("/api/employees/3");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("still holds", (await response.ReadJsonAsync()).Str("message"));

        // The refusal changed nothing.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/employees/3")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/assets/{asset.Id()}")).StatusCode);
    }

    [Fact]
    public async Task Admin_deletes_an_employee_with_their_login_and_requests()
    {
        var admin = await factory.AdminAsync();
        var created = await admin.PostOkAsync("/api/employees", new
        {
            fullName = "Temp Person",
            email = "temp.person@assetpro.local",
            departmentId = 1,
            designationId = 1,
            login = new { password = "Temp@12345", role = "User" }
        });

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/employees/{created.Id()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/employees/{created.Id()}")).StatusCode);

        // The sign-in went with them.
        var login = await factory.CreateClient().PostJsonAsync("/api/auth/login",
            new { email = "temp.person@assetpro.local", password = "Temp@12345" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_delete_their_own_account()
    {
        var admin = await factory.AdminAsync();
        var me = await admin.GetJsonAsync("/api/users");
        var adminId = me.GetProperty("items").EnumerateArray()
            .First(u => u.Str("email") == ApiFactory.AdminEmail).Id();

        var response = await admin.DeleteAsync($"/api/users/{adminId}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("your own", (await response.ReadJsonAsync()).Str("message"));
    }

    [Fact]
    public async Task Inventory_item_with_assets_cannot_be_deleted()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();
        // Each test asset gets a unique model, so its inventory item is unique too.
        var model = asset.Str("model");
        var itemId = (await admin.GetJsonAsync("/api/inventory?pageSize=500"))
            .GetProperty("items").EnumerateArray().First(i => i.Str("model") == model).Id();

        var response = await admin.DeleteAsync($"/api/inventory/{itemId}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("asset(s) registered", (await response.ReadJsonAsync()).Str("message"));
    }
}
