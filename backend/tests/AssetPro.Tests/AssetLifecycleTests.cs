using System.Net;
using System.Text.Json;
using AssetPro.Tests.Infrastructure;

namespace AssetPro.Tests;

public sealed class AssetLifecycleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int Muhammad = 2;
    private const int Fatima = 3;
    private const int InactiveJames = 13;

    private static async Task<JsonElement> GetAsset(HttpClient admin, int id) => (await admin.GetJsonAsync($"/api/assets/{id}")).GetProperty("asset");

    private static async Task<int> InStock(HttpClient admin, int itemId) =>
        (await admin.GetJsonAsync($"/api/inventory/{itemId}")).GetProperty("inStock").GetInt32();

    [Fact]
    public async Task Create_generates_tag_and_records_history()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();

        Assert.StartsWith("LAP-", asset.Str("assetTag"));
        Assert.Equal("InStock", asset.Str("status"));
        var history = await admin.GetJsonAsync($"/api/assets/{asset.Id()}/history");
        Assert.Contains(history.EnumerateArray(), h => h.Str("action") == "Created");
    }

    [Fact]
    public async Task Duplicate_tag_and_serial_are_rejected()
    {
        var admin = await factory.AdminAsync();
        var serial = "SN-" + Guid.NewGuid().ToString("N")[..8];
        var tag = "DUP-" + Guid.NewGuid().ToString("N")[..6];
        await admin.CreateAssetAsync(serial, tag);

        var dupSerial = await admin.PostJsonAsync("/api/assets", new { name = "Another", assetTypeId = 1, serialNumber = serial.ToLowerInvariant() });
        Assert.Equal(HttpStatusCode.BadRequest, dupSerial.StatusCode);
        Assert.True((await dupSerial.ReadJsonAsync()).GetProperty("errors").TryGetProperty("serialNumber", out _));

        var dupTag = await admin.PostJsonAsync("/api/assets", new { name = "Another", assetTypeId = 1, assetTag = tag });
        Assert.Equal(HttpStatusCode.BadRequest, dupTag.StatusCode);
    }

    [Fact]
    public async Task Empty_and_invalid_fields_fail_validation()
    {
        var admin = await factory.AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync("/api/assets", new { name = "", assetTypeId = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync("/api/assets", new { name = "Future", assetTypeId = 1, purchaseDate = "2099-01-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync("/api/assets", new { name = "Bad", assetTypeId = 1, purchaseCost = -5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync("/api/assets", new { name = new string('x', 500), assetTypeId = 1 })).StatusCode);
    }

    [Fact]
    public async Task Unicode_and_special_characters_round_trip()
    {
        var admin = await factory.AdminAsync();
        var name = "Écran 显示器 <script>alert(1)</script> ‘quoted’";
        var created = await admin.PostOkAsync("/api/assets", new { name, assetTypeId = 3 });
        Assert.Equal(name, created.Str("name"));
    }

    [Fact]
    public async Task Assign_then_return_updates_status_inventory_and_history()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();
        var itemId = (await admin.GetJsonAsync($"/api/assets/{asset.Id()}")).GetProperty("inventoryItemId").GetInt32();
        Assert.Equal(1, await InStock(admin, itemId));

        var assigned = await admin.PostOkAsync($"/api/assets/{asset.Id()}/assign", new { employeeId = Muhammad, notes = "Onboarding" });
        Assert.Equal("Assigned", assigned.Str("status"));
        Assert.Equal("Muhammad Ali", assigned.Str("assignedTo"));
        Assert.Equal(0, await InStock(admin, itemId));

        // Cannot be assigned twice.
        var again = await admin.PostJsonAsync($"/api/assets/{asset.Id()}/assign", new { employeeId = Fatima });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var returned = await admin.PostOkAsync($"/api/assets/{asset.Id()}/return", new { condition = "Good", resultingStatus = "InStock" });
        Assert.Equal("InStock", returned.Str("status"));
        Assert.Equal(JsonValueKind.Null, returned.GetProperty("assignedTo").ValueKind);
        Assert.Equal(1, await InStock(admin, itemId));

        // Returning an unassigned asset fails.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJsonAsync($"/api/assets/{asset.Id()}/return", new { condition = "Good" })).StatusCode);

        // The original assignment is preserved.
        var detail = await admin.GetJsonAsync($"/api/assets/{asset.Id()}");
        var assignment = Assert.Single(detail.GetProperty("assignments").EnumerateArray());
        Assert.NotEqual(JsonValueKind.Null, assignment.GetProperty("returnedAt").ValueKind);

        var actions = (await admin.GetJsonAsync($"/api/assets/{asset.Id()}/history")).EnumerateArray().Select(h => h.Str("action")).ToList();
        Assert.Contains("Assigned", actions);
        Assert.Contains("Returned", actions);

        var txns = await admin.GetJsonAsync($"/api/inventory/transactions?filters[inventoryItemId]={itemId}");
        Assert.Contains(txns.GetProperty("items").EnumerateArray(), t => t.Str("type") == "StockOut");
        Assert.Contains(txns.GetProperty("items").EnumerateArray(), t => t.Str("type") == "Return");
    }

    [Fact]
    public async Task Inactive_employee_cannot_receive_assets()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();
        var response = await admin.PostJsonAsync($"/api/assets/{asset.Id()}/assign", new { employeeId = InactiveJames });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Scrapped_asset_cannot_be_assigned_or_scrapped_again()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();
        var scrapped = await admin.PostOkAsync($"/api/assets/{asset.Id()}/scrap", new { reason = "Motherboard dead" });
        Assert.Equal("Scrapped", scrapped.Str("status"));

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJsonAsync($"/api/assets/{asset.Id()}/assign", new { employeeId = Muhammad })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJsonAsync($"/api/assets/{asset.Id()}/scrap", new { reason = "Again" })).StatusCode);

        var detail = await admin.GetJsonAsync($"/api/assets/{asset.Id()}");
        Assert.Equal("Motherboard dead", detail.GetProperty("scrap").Str("reason"));
        var report = await admin.GetJsonAsync($"/api/reports/scrap?filters[assetId]={asset.Id()}");
        Assert.Equal(1, report.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Assigned_asset_must_be_returned_before_scrap()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();
        await admin.PostOkAsync($"/api/assets/{asset.Id()}/assign", new { employeeId = Muhammad });
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJsonAsync($"/api/assets/{asset.Id()}/scrap", new { reason = "Broken" })).StatusCode);

        // Return straight to scrap works and keeps both records.
        var result = await admin.PostOkAsync($"/api/assets/{asset.Id()}/return",
            new { condition = "Critical", resultingStatus = "Scrapped", scrapReason = "Dropped from a height", damageDetails = "Cracked chassis" });
        Assert.Equal("Scrapped", result.Str("status"));
    }

    [Fact]
    public async Task Retired_asset_cannot_be_assigned()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();
        await admin.PostOkAsync($"/api/assets/{asset.Id()}/status", new { status = "Retired", notes = "End of life" });
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJsonAsync($"/api/assets/{asset.Id()}/assign", new { employeeId = Muhammad })).StatusCode);
        // Retired → InStock is not an allowed manual transition.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJsonAsync($"/api/assets/{asset.Id()}/status", new { status = "InStock" })).StatusCode);
    }

    [Fact]
    public async Task Asset_under_repair_cannot_be_assigned_until_repaired()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();
        await admin.PostOkAsync($"/api/assets/{asset.Id()}/repair", new { action = "Start", notes = "Screen replacement" });
        Assert.Equal("UnderRepair", (await GetAsset(admin, asset.Id())).Str("status"));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJsonAsync($"/api/assets/{asset.Id()}/assign", new { employeeId = Muhammad })).StatusCode);

        var repaired = await admin.PostOkAsync($"/api/assets/{asset.Id()}/repair", new { action = "Complete" });
        Assert.Equal("InStock", repaired.Str("status"));
        await admin.PostOkAsync($"/api/assets/{asset.Id()}/assign", new { employeeId = Muhammad });
    }

    [Fact]
    public async Task Repair_of_assigned_asset_returns_it_to_the_holder()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();
        await admin.PostOkAsync($"/api/assets/{asset.Id()}/assign", new { employeeId = Fatima });
        await admin.PostOkAsync($"/api/assets/{asset.Id()}/repair", new { action = "Start" });
        var done = await admin.PostOkAsync($"/api/assets/{asset.Id()}/repair", new { action = "Complete" });
        Assert.Equal("Assigned", done.Str("status"));
        Assert.Equal("Fatima Noor", done.Str("assignedTo"));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJsonAsync($"/api/assets/{asset.Id()}/repair", new { action = "Complete" })).StatusCode);
    }

    [Fact]
    public async Task Component_replacement_is_recorded_without_touching_assignment()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();
        await admin.PostOkAsync($"/api/assets/{asset.Id()}/assign", new { employeeId = Muhammad });

        await admin.PostOkAsync($"/api/assets/{asset.Id()}/replace-component", new
        {
            component = "RAM", oldComponent = "8GB RAM", newComponent = "16GB RAM", reason = "RAM failure",
            notes = "RAM replaced due to hardware failure."
        });

        var detail = await admin.GetJsonAsync($"/api/assets/{asset.Id()}");
        Assert.Equal("Assigned", detail.GetProperty("asset").Str("status"));
        Assert.Equal(Muhammad, detail.GetProperty("currentAssignment").GetProperty("employeeId").GetInt32());
        var replacement = Assert.Single(detail.GetProperty("replacements").EnumerateArray());
        Assert.Equal("16GB RAM", replacement.Str("newComponent"));

        var history = await admin.GetJsonAsync($"/api/assets/{asset.Id()}/history");
        var entry = history.EnumerateArray().First(h => h.Str("action") == "ComponentReplaced");
        Assert.Equal(Muhammad, entry.GetProperty("employeeId").GetInt32());
    }

    [Fact]
    public async Task Damaged_return_cannot_go_straight_to_stock()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();
        await admin.PostOkAsync($"/api/assets/{asset.Id()}/assign", new { employeeId = Muhammad });
        var response = await admin.PostJsonAsync($"/api/assets/{asset.Id()}/return", new { condition = "Damaged", resultingStatus = "InStock" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Future_assignment_dates_are_rejected()
    {
        var admin = await factory.AdminAsync();
        var asset = await admin.CreateAssetAsync();
        var response = await admin.PostJsonAsync($"/api/assets/{asset.Id()}/assign", new { employeeId = Muhammad, assignedAt = DateTime.UtcNow.AddDays(3) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Grid_query_filters_sorts_and_pages()
    {
        var admin = await factory.AdminAsync();
        var page = await admin.GetJsonAsync("/api/assets?page=1&pageSize=5&sort=assetTag&dir=asc&filters[status]=Assigned&filters[assetType]=lap");
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(5, items.Count);
        Assert.All(items, i => Assert.Equal("Assigned", i.Str("status")));
        Assert.All(items, i => Assert.Equal("Laptop", i.Str("assetType")));
        var tags = items.Select(i => i.Str("assetTag")).ToList();
        Assert.Equal(tags.OrderBy(t => t, StringComparer.Ordinal), tags);
        Assert.True(page.GetProperty("totalCount").GetInt32() >= 5);

        var search = await admin.GetJsonAsync("/api/assets?search=muhammad&pageSize=50");
        Assert.All(search.GetProperty("items").EnumerateArray(), i => Assert.Equal("Muhammad Ali", i.Str("assignedTo")));

        // Unknown columns and SQL-looking input are harmless.
        var weird = await admin.GetAsync("/api/assets?sort=password;DROP TABLE Assets&filters[nope]=1&search=%27%20OR%201=1--");
        Assert.Equal(HttpStatusCode.OK, weird.StatusCode);
        Assert.Equal(0, (await weird.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
    }
}
