using System.Net;
using System.Net.Http.Json;
using AssetPro.Tests.Infrastructure;

namespace AssetPro.Tests;

public sealed class EmployeeAndMasterDataTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Create_employee_with_login_lets_them_sign_in()
    {
        var admin = await factory.AdminAsync();
        var email = $"hire{Guid.NewGuid():N}"[..14] + "@assetpro.local";
        var created = await admin.PostOkAsync("/api/employees", new
        {
            fullName = "Nadia Hussain", email, departmentId = 1, designationId = 2,
            login = new { role = "User", password = "Welcome123" }
        });
        Assert.StartsWith("EMP-", created.Str("employeeCode"));
        Assert.Equal("User", created.Str("role"));

        var client = await factory.ClientForAsync(email, "Welcome123");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/profile")).StatusCode);
    }

    [Fact]
    public async Task Duplicate_or_invalid_employee_email_is_rejected()
    {
        var admin = await factory.AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync("/api/employees", new { fullName = "Dup", email = ApiFactory.UserEmail.ToUpperInvariant() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync("/api/employees", new { fullName = "Bad", email = "bad-email" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync("/api/employees", new { fullName = "Weak", email = "weak@assetpro.local", login = new { role = "User", password = "short" } })).StatusCode);
    }

    [Fact]
    public async Task Employee_holding_assets_cannot_be_deactivated()
    {
        var admin = await factory.AdminAsync();
        var profile = await admin.GetJsonAsync("/api/employees/2");
        var e = profile.GetProperty("employee");
        var response = await admin.PutAsJsonAsync("/api/employees/2", new
        {
            fullName = e.Str("fullName"), email = e.Str("email"), status = "Inactive"
        }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Smart_dropdown_add_new_persists_and_rejects_duplicates()
    {
        var admin = await factory.AdminAsync();
        var name = "Marketing Ops " + Guid.NewGuid().ToString("N")[..4];
        var created = await admin.PostOkAsync("/api/lookups/departments", new { name });
        Assert.Equal(name, created.Str("name"));

        var list = await admin.GetJsonAsync("/api/lookups/departments");
        Assert.Contains(list.EnumerateArray(), d => d.Str("name") == name);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync("/api/lookups/departments", new { name = name.ToUpperInvariant() })).StatusCode);
    }

    [Fact]
    public async Task Referenced_master_data_cannot_be_deleted()
    {
        var admin = await factory.AdminAsync();
        var response = await admin.DeleteAsync("/api/lookups/departments/1");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var unused = await admin.PostOkAsync("/api/lookups/brands", new { name = "Brand " + Guid.NewGuid().ToString("N")[..5] });
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/lookups/brands/{unused.Id()}")).StatusCode);
    }

    [Fact]
    public async Task Dashboard_metrics_come_from_the_database()
    {
        var admin = await factory.AdminAsync();
        var before = await admin.GetJsonAsync("/api/dashboard");
        await admin.CreateAssetAsync();
        var after = await admin.GetJsonAsync("/api/dashboard");
        Assert.Equal(before.GetProperty("availableAssets").GetInt32() + 1, after.GetProperty("availableAssets").GetInt32());
        Assert.Equal(6, after.GetProperty("requestTrend").GetArrayLength());
    }
}
