using System.Net;
using AssetPro.Tests.Infrastructure;

namespace AssetPro.Tests;

public sealed class RequestWorkflowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<int> CreateRequest(HttpClient user, string comments = "My mouse scroll wheel is broken")
    {
        var created = await user.PostOkAsync("/api/requests", new { type = "NewAsset", comments });
        Assert.Equal("Submitted", created.GetProperty("request").Str("status"));
        return created.GetProperty("request").Id();
    }

    [Fact]
    public async Task Full_workflow_reaches_done_with_timestamps()
    {
        var admin = await factory.AdminAsync();
        var user = await factory.UserAsync();
        var id = await CreateRequest(user);

        await admin.PostOkAsync($"/api/requests/{id}/approve", new { });
        await admin.PostOkAsync($"/api/requests/{id}/start", new { });
        await admin.PostOkAsync($"/api/requests/{id}/deliver", new { note = "Delivered to desk" });
        await user.PostOkAsync($"/api/requests/{id}/check", new { });
        await user.PostOkAsync($"/api/requests/{id}/receive", new { note = "All good" });
        var done = await admin.PostOkAsync($"/api/requests/{id}/complete", new { });

        Assert.Equal("Done", done.GetProperty("request").Str("status"));
        var statuses = done.GetProperty("history").EnumerateArray().Select(h => h.Str("toStatus")).ToList();
        Assert.Equal(["Submitted", "Approved", "InProgress", "Delivered", "Checking", "Received", "Done"], statuses);
        Assert.Empty(done.GetProperty("allowedActions").EnumerateArray());
    }

    [Fact]
    public async Task Statuses_cannot_be_skipped()
    {
        var admin = await factory.AdminAsync();
        var user = await factory.UserAsync();
        var id = await CreateRequest(user);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJsonAsync($"/api/requests/{id}/deliver", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJsonAsync($"/api/requests/{id}/complete", new { })).StatusCode);
        await admin.PostOkAsync($"/api/requests/{id}/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJsonAsync($"/api/requests/{id}/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJsonAsync($"/api/requests/{id}/reject", new { note = "late" })).StatusCode);
    }

    [Fact]
    public async Task Only_requester_can_check_and_receive()
    {
        var admin = await factory.AdminAsync();
        var user = await factory.UserAsync();
        var id = await CreateRequest(user);
        await admin.PostOkAsync($"/api/requests/{id}/approve", new { });
        await admin.PostOkAsync($"/api/requests/{id}/start", new { });
        await admin.PostOkAsync($"/api/requests/{id}/deliver", new { });

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostJsonAsync($"/api/requests/{id}/check", new { })).StatusCode);
        var detail = await user.GetJsonAsync($"/api/requests/{id}");
        Assert.Equal(["check"], detail.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()!).ToArray());
    }

    [Fact]
    public async Task Standard_user_cannot_approve_own_request()
    {
        var user = await factory.UserAsync();
        var id = await CreateRequest(user);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostJsonAsync($"/api/requests/{id}/approve", new { })).StatusCode);
    }

    [Fact]
    public async Task Reject_requires_reason_and_is_final()
    {
        var approver = await factory.UserAsync(ApiFactory.ApproverEmail);
        var user = await factory.UserAsync();
        var id = await CreateRequest(user);

        Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostJsonAsync($"/api/requests/{id}/reject", new { })).StatusCode);
        var rejected = await approver.PostOkAsync($"/api/requests/{id}/reject", new { note = "Budget freeze" });
        Assert.Equal("Rejected", rejected.GetProperty("request").Str("status"));
        // An approver without Requests.Process cannot move it on either way.
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.PostJsonAsync($"/api/requests/{id}/start", new { })).StatusCode);
    }

    [Fact]
    public async Task Repair_request_needs_an_asset_and_logs_history()
    {
        var user = await factory.UserAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostJsonAsync("/api/requests", new { type = "Repair", comments = "It is broken" })).StatusCode);

        var profile = await user.GetJsonAsync("/api/me/profile");
        var assetId = profile.GetProperty("assets").EnumerateArray()
            .First(a => a.Str("assetType") == "Headset" || a.Str("assetType") == "Keyboard").Id();
        await user.PostOkAsync("/api/requests", new { type = "Repair", assetId, comments = "Crackling noise" });
        var history = await user.GetJsonAsync($"/api/assets/{assetId}/history");
        Assert.Contains(history.EnumerateArray(), h => h.Str("action") == "RepairRequested");

        // Duplicate open request for the same asset/type is blocked.
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostJsonAsync("/api/requests", new { type = "Repair", assetId, comments = "Still crackling" })).StatusCode);
    }

    [Fact]
    public async Task Request_comments_are_length_limited()
    {
        var user = await factory.UserAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostJsonAsync("/api/requests", new { type = "Other", comments = new string('a', 2001) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostJsonAsync("/api/requests", new { type = "Other", comments = "" })).StatusCode);
    }

    [Fact]
    public async Task Workflow_changes_are_written_to_activity_log()
    {
        var admin = await factory.AdminAsync();
        var user = await factory.UserAsync();
        var id = await CreateRequest(user, "Need a docking station please");
        await admin.PostOkAsync($"/api/requests/{id}/approve", new { });

        var log = await admin.GetJsonAsync("/api/activity-logs?pageSize=20&filters[action]=RequestApproved");
        Assert.Contains(log.GetProperty("items").EnumerateArray(), e => e.Str("entityId") == id.ToString());
    }
}
