using System.Net;
using AssetPro.Tests.Infrastructure;

namespace AssetPro.Tests;

public sealed class InventoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static object StockIn(int quantity, string model) => new
    {
        assetTypeId = 4, brandId = 5, model, name = $"Logitech {model} Mouse", quantity, unitCost = 19.5m, reorderLevel = 2
    };

    [Fact]
    public async Task Stock_in_creates_tracked_units()
    {
        var admin = await factory.AdminAsync();
        var model = "M-" + Guid.NewGuid().ToString("N")[..6];
        var result = await admin.PostOkAsync("/api/inventory/stock-in", StockIn(10, model));

        Assert.Equal(10, result.GetProperty("assetTags").GetArrayLength());
        Assert.Equal(10, result.GetProperty("inStockAfter").GetInt32());
        var item = await admin.GetJsonAsync($"/api/inventory/{result.GetProperty("inventoryItemId").GetInt32()}");
        Assert.Equal(10, item.GetProperty("inStock").GetInt32());
        Assert.Equal(10, item.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(501)]
    public async Task Invalid_quantities_are_rejected(int quantity)
    {
        var admin = await factory.AdminAsync();
        var response = await admin.PostJsonAsync("/api/inventory/stock-in", StockIn(quantity, "Q" + quantity));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Stock_out_decreases_stock_and_never_goes_negative()
    {
        var admin = await factory.AdminAsync();
        var model = "M-" + Guid.NewGuid().ToString("N")[..6];
        var stock = await admin.PostOkAsync("/api/inventory/stock-in", StockIn(3, model));
        var itemId = stock.GetProperty("inventoryItemId").GetInt32();

        var issued = await admin.PostOkAsync("/api/inventory/stock-out", new { inventoryItemId = itemId, employeeId = 2, quantity = 2 });
        Assert.Equal(1, issued.GetProperty("inStockAfter").GetInt32());

        var tooMany = await admin.PostJsonAsync("/api/inventory/stock-out", new { inventoryItemId = itemId, employeeId = 3, quantity = 2 });
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);

        // The failed request changed nothing.
        var item = await admin.GetJsonAsync($"/api/inventory/{itemId}");
        Assert.Equal(1, item.GetProperty("inStock").GetInt32());
        Assert.Equal(2, item.GetProperty("assigned").GetInt32());
    }

    [Fact]
    public async Task Stock_in_rejects_duplicate_serials()
    {
        var admin = await factory.AdminAsync();
        var response = await admin.PostJsonAsync("/api/inventory/stock-in", new
        {
            assetTypeId = 1, name = "Laptop batch", quantity = 2, serialNumbers = new[] { "SAME-1", "same-1" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
