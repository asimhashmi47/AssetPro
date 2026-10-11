using AssetPro.Api.Auth;
using AssetPro.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AssetPro.Api.Data;

/// <summary>
/// Seeds the default administrator (always, if no users exist) and - optionally - realistic demo data
/// spread over the last six months so the dashboard looks alive on first start.
/// DEMO CREDENTIALS ONLY: change or remove them before using AssetPro with real data.
/// </summary>
public sealed class DemoSeeder(AppDbContext db, IPasswordHasher<User> hasher, ILogger<DemoSeeder> logger)
{
    public const string AdminEmail = "admin@assetpro.local";
    public const string AdminPassword = "Admin@123";
    public const string DemoUserPassword = "User@1234";

    private readonly Random _random = new(42);
    private readonly DateTime _now = DateTime.UtcNow;
    private User _admin = null!;

    public async Task SeedAsync(bool includeDemoData, int historyDays = 0)
    {
        if (await db.Users.AnyAsync()) return;

        logger.LogInformation("Seeding database (demo data: {Demo}, history: {Days} days)", includeDemoData, historyDays);
        await using var tx = await db.Database.BeginTransactionAsync();

        var departments = Lookups<Department>("IT", "HR", "Finance", "Marketing", "Operations", "Sales", "Engineering");
        var designations = Lookups<Designation>("IT Administrator", "Software Engineer", "Senior Software Engineer", "HR Manager",
            "Accountant", "Marketing Specialist", "Operations Lead", "Sales Executive", "IT Support Specialist", "Product Designer");
        var locations = Lookups<Location>("Head Office - Floor 1", "Head Office - Floor 2", "IT Store Room", "Warehouse", "Remote");
        var brands = Lookups<Brand>("Dell", "HP", "Lenovo", "Apple", "Logitech", "Samsung", "LG", "Jabra");
        var types = new[]
        {
            ("Laptop", "LAP"), ("Desktop Computer", "DSK"), ("Monitor / LED", "MON"), ("Mouse", "MOU"), ("Keyboard", "KEY"),
            ("Headset", "HDS"), ("Mobile Phone", "MOB"), ("Printer", "PRN"), ("Tablet", "TAB"), ("Other", "OTH")
        }.ToDictionary(t => t.Item1, t => new AssetType { Name = t.Item1, TagPrefix = t.Item2 });
        db.AddRange(departments.Values);
        db.AddRange(designations.Values);
        db.AddRange(locations.Values);
        db.AddRange(brands.Values);
        db.AddRange(types.Values);

        var adminEmployee = NewEmployee("EMP-0001", "Ayesha Khan", AdminEmail, departments["IT"], designations["IT Administrator"], 175);
        db.Employees.Add(adminEmployee);
        _admin = new User { Email = AdminEmail, NormalizedEmail = AdminEmail.ToUpperInvariant(), Role = UserRole.Admin, Employee = adminEmployee };
        _admin.PasswordHash = hasher.HashPassword(_admin, AdminPassword);
        db.Users.Add(_admin);

        foreach (var (key, value) in Features.Admin.SettingsController.Defaults)
            db.Settings.Add(new AppSetting { Key = key, Value = value });

        await db.SaveChangesAsync();

        if (includeDemoData)
        {
            await SeedDemoAsync(departments, designations, locations, brands, types);
            await new HistorySimulator(db, hasher, logger).RunAsync(historyDays);
        }

        await tx.CommitAsync();
    }

    private async Task SeedDemoAsync(
        Dictionary<string, Department> departments, Dictionary<string, Designation> designations,
        Dictionary<string, Location> locations, Dictionary<string, Brand> brands, Dictionary<string, AssetType> types)
    {
        // ---- Employees & users ----
        var people = new (string Name, string Dept, string Title, string Email)[]
        {
            ("Muhammad Ali", "Engineering", "Senior Software Engineer", "muhammad.ali"),
            ("Fatima Noor", "Finance", "Accountant", "fatima.noor"),
            ("Daniel Carter", "Operations", "Operations Lead", "daniel.carter"),
            ("Priya Sharma", "Engineering", "Software Engineer", "priya.sharma"),
            ("Omar Farooq", "Sales", "Sales Executive", "omar.farooq"),
            ("Emily Chen", "Marketing", "Marketing Specialist", "emily.chen"),
            ("Hassan Raza", "IT", "IT Support Specialist", "hassan.raza"),
            ("Sophia Martinez", "HR", "HR Manager", "sophia.martinez"),
            ("Bilal Ahmed", "Engineering", "Product Designer", "bilal.ahmed"),
            ("Zara Iqbal", "Sales", "Sales Executive", "zara.iqbal"),
            ("Lucas Meyer", "Engineering", "Software Engineer", "lucas.meyer"),
            ("James Wilson", "Finance", "Accountant", "james.wilson")
        };
        var employees = new List<Employee>();
        for (var i = 0; i < people.Length; i++)
        {
            var p = people[i];
            var e = NewEmployee($"EMP-{i + 2:D4}", p.Name, $"{p.Email}@assetpro.local", departments[p.Dept], designations[p.Title], 170 - i * 6);
            employees.Add(e);
        }
        employees[^1].Status = EmployeeStatus.Inactive;
        db.Employees.AddRange(employees);
        await db.SaveChangesAsync();

        // Three demo sign-ins. Daniel is an operations lead who may approve requests.
        AddUser(employees[0], []);
        AddUser(employees[1], []);
        AddUser(employees[2], [Permissions.RequestsApprove, Permissions.RequestsView, Permissions.DashboardView]);
        await db.SaveChangesAsync();

        // ---- Inventory ----
        var catalog = new (string Type, string Brand, string Model, string Name, int Qty, decimal Cost, bool Serial, int Reorder)[]
        {
            ("Laptop", "Dell", "Latitude 5440", "Dell Latitude 5440", 8, 1150m, true, 2),
            ("Laptop", "Lenovo", "ThinkPad T14 Gen 4", "Lenovo ThinkPad T14", 5, 1290m, true, 1),
            ("Laptop", "Apple", "MacBook Pro 14 M3", "MacBook Pro 14\"", 3, 1999m, true, 1),
            ("Desktop Computer", "HP", "EliteDesk 800 G9", "HP EliteDesk 800", 3, 890m, true, 1),
            ("Monitor / LED", "Dell", "P2422H", "Dell 24\" Monitor P2422H", 10, 219m, true, 3),
            ("Monitor / LED", "LG", "27UL500-W", "LG 27\" 4K Monitor", 4, 329m, true, 1),
            ("Mouse", "Logitech", "M185", "Logitech M185 Wireless Mouse", 20, 19m, false, 5),
            ("Keyboard", "Logitech", "K120", "Logitech K120 Keyboard", 15, 22m, false, 4),
            ("Headset", "Jabra", "Evolve2 40", "Jabra Evolve2 40 Headset", 6, 129m, true, 2),
            ("Mobile Phone", "Samsung", "Galaxy A54", "Samsung Galaxy A54", 4, 449m, true, 1),
            ("Printer", "HP", "LaserJet Pro M404dn", "HP LaserJet Pro M404", 2, 349m, true, 0),
            ("Tablet", "Apple", "iPad 10th Gen", "Apple iPad 10.9\"", 3, 499m, true, 1)
        };

        var assetsByType = new Dictionary<string, List<Asset>>();
        var counters = new Dictionary<string, int>();
        foreach (var c in catalog)
        {
            var type = types[c.Type];
            var item = new InventoryItem { Name = c.Name, AssetType = type, Brand = brands[c.Brand], Model = c.Model, ReorderLevel = c.Reorder };
            db.InventoryItems.Add(item);
            var receivedDaysAgo = _random.Next(175, 186);
            var purchase = DateOnly.FromDateTime(_now.AddDays(-receivedDaysAgo - 5));
            var units = new List<Asset>();
            for (var i = 0; i < c.Qty; i++)
            {
                counters[type.TagPrefix] = counters.GetValueOrDefault(type.TagPrefix) + 1;
                var asset = new Asset
                {
                    AssetTag = $"{type.TagPrefix}-{counters[type.TagPrefix]:D5}",
                    Name = c.Name,
                    AssetType = type,
                    Brand = brands[c.Brand],
                    Model = c.Model,
                    SerialNumber = c.Serial ? $"{c.Brand[..2].ToUpperInvariant()}{_random.Next(100000, 999999)}{(char)('A' + i % 26)}{counters[type.TagPrefix]:D3}" : null,
                    PurchaseDate = purchase,
                    PurchaseCost = c.Cost,
                    WarrantyExpiry = c.Type is "Mouse" or "Keyboard" ? null : purchase.AddYears(c.Type == "Headset" ? 1 : 3).AddDays(c.Type == "Headset" ? -150 : 0),
                    Condition = AssetCondition.New,
                    Status = AssetStatus.InStock,
                    Location = c.Type is "Printer" ? locations["Head Office - Floor 1"] : locations["IT Store Room"],
                    InventoryItem = item,
                    CreatedAt = _now.AddDays(-receivedDaysAgo),
                    UpdatedAt = _now.AddDays(-receivedDaysAgo)
                };
                db.Assets.Add(asset);
                units.Add(asset);
            }
            assetsByType.TryAdd(c.Type, []);
            assetsByType[c.Type].AddRange(units);
            await db.SaveChangesAsync();

            foreach (var asset in units)
                History(asset, AssetAction.StockIn, null, AssetStatus.InStock, null, "Received into inventory", asset.CreatedAt);
            Txn(item, InventoryTransactionType.StockIn, c.Qty, c.Qty, null, null, $"Purchase order received ({units[0].AssetTag} – {units[^1].AssetTag})", units[0].CreatedAt);
            Activity("InventoryAdded", $"Stocked in {c.Qty} × {c.Name}", "InventoryItem", item.Id, units[0].CreatedAt);
        }
        await db.SaveChangesAsync();

        // ---- Assignments (spread over the last ~5 months) ----
        var active = employees.Take(11).ToList();
        var laptops = assetsByType["Laptop"];
        var monitors = assetsByType["Monitor / LED"];
        var mice = assetsByType["Mouse"];
        var keyboards = assetsByType["Keyboard"];
        var headsets = assetsByType["Headset"];
        var phones = assetsByType["Mobile Phone"];
        var tablets = assetsByType["Tablet"];
        var desktops = assetsByType["Desktop Computer"];

        for (var i = 0; i < active.Count; i++)
        {
            var e = active[i];
            var day = 150 - i * 11;
            Assign(laptops[i], e, day);
            if (i < 9) Assign(monitors[i], e, day - 1);
            Assign(mice[i], e, day - 1);
            if (i < 9) Assign(keyboards[i], e, day - 2);
            if (i % 2 == 0 && i / 2 < headsets.Count) Assign(headsets[i / 2], e, day - 3);
        }
        Assign(phones[0], active[2], 95);
        Assign(phones[1], active[4], 80);
        Assign(tablets[0], active[5], 60);
        Assign(desktops[0], active[6], 120);

        // James (now inactive) returned everything before leaving.
        var james = employees[^1];
        var jamesLaptop = laptops[12];
        var a1 = Assign(jamesLaptop, james, 160);
        await db.SaveChangesAsync();
        Return(a1, jamesLaptop, 40, AssetCondition.Good, AssetStatus.InStock, "Employee left the company - laptop wiped and restocked");

        // Keyboard returned damaged by Omar, then scrapped.
        var omarKeyboard = keyboards[4];
        var aKb = await db.AssetAssignments.FirstAsync(x => x.AssetId == omarKeyboard.Id && x.ReturnedAt == null);
        Return(aKb, omarKeyboard, 35, AssetCondition.Critical, AssetStatus.Damaged, "Coffee spill - several keys not working", "Liquid damage");
        Scrap(omarKeyboard, 30, "Liquid damage, not economical to repair");

        // A dead mouse from stock scrapped.
        Scrap(mice[18], 20, "Sensor failure (dead on arrival)");
        // An older monitor retired.
        StatusChange(monitors[9], AssetStatus.Retired, AssetAction.Retired, 45, "Retired - replaced by 4K displays");
        // Laptop under repair while still assigned to Priya.
        Repair(laptops[3], active[3], 12, start: true, "Battery swelling - sent to Dell service centre");
        // A monitor in stock marked damaged.
        StatusChange(monitors[11], AssetStatus.Damaged, AssetAction.Damaged, 9, "Dead pixels found during inspection");
        monitors[11].Condition = AssetCondition.Damaged;

        // Component replacement on Muhammad's laptop (the classic RAM upgrade example).
        var muhammadLaptop = laptops[0];
        db.ComponentReplacements.Add(new ComponentReplacement
        {
            AssetId = muhammadLaptop.Id, Component = "RAM", OldComponent = "8GB DDR5", NewComponent = "16GB DDR5",
            Reason = "RAM failure", ReplacedOn = DateOnly.FromDateTime(_now.AddDays(-55)), PerformedById = _admin.Id,
            Notes = "RAM replaced due to hardware failure.", CreatedAt = _now.AddDays(-55)
        });
        History(muhammadLaptop, AssetAction.ComponentReplaced, AssetStatus.Assigned, AssetStatus.Assigned, active[0].Id,
            "RAM: 8GB DDR5 → 16GB DDR5 (RAM failure). RAM replaced due to hardware failure.", _now.AddDays(-55));
        Activity("ComponentReplaced", $"Replaced RAM on {muhammadLaptop.AssetTag}", "Asset", muhammadLaptop.Id, _now.AddDays(-55));

        // Repair cycle completed on Fatima's laptop.
        Repair(laptops[1], active[1], 70, start: true, "Keyboard backlight flickering");
        Repair(laptops[1], active[1], 64, start: false, "Keyboard assembly replaced under warranty");
        db.ComponentReplacements.Add(new ComponentReplacement
        {
            AssetId = laptops[1].Id, Component = "Keyboard", OldComponent = "Original keyboard", NewComponent = "Dell OEM backlit keyboard",
            Reason = "Backlight failure", ReplacedOn = DateOnly.FromDateTime(_now.AddDays(-64)), PerformedById = _admin.Id,
            Notes = "Warranty replacement", CreatedAt = _now.AddDays(-64)
        });
        History(laptops[1], AssetAction.ComponentReplaced, AssetStatus.Assigned, AssetStatus.Assigned, active[1].Id,
            "Keyboard: Original keyboard → Dell OEM backlit keyboard (Backlight failure)", _now.AddDays(-64).AddMinutes(5));
        await db.SaveChangesAsync();

        // ---- Requests ----
        var flow = new[] { RequestStatus.Approved, RequestStatus.InProgress, RequestStatus.Delivered, RequestStatus.Checking, RequestStatus.Received, RequestStatus.Done };
        var requests = new (Employee Who, RequestType Type, Asset? Asset, string Comments, int DaysAgo, int Steps, bool Rejected)[]
        {
            (active[3], RequestType.Repair, laptops[3], "Laptop battery is swelling and the trackpad is lifting.", 14, 2, false),
            (active[0], RequestType.Replacement, mice[0], "Mouse scroll wheel is skipping, please replace.", 9, 0, false),
            (active[1], RequestType.NewAsset, null, "Need a second monitor for month-end reconciliation work.", 6, 0, false),
            (active[5], RequestType.NewAsset, null, "Requesting a headset for client calls.", 20, 3, false),
            (active[2], RequestType.Repair, phones[0], "Phone screen cracked after a fall.", 40, 6, false),
            (active[4], RequestType.Other, null, "Can I get a laptop stand for ergonomic reasons?", 25, 0, true),
            (active[6], RequestType.Return, desktops[0], "Moving to a laptop, returning the desktop.", 4, 1, false),
            (active[8], RequestType.NewAsset, null, "Drawing tablet for design work.", 75, 6, false),
            (active[9], RequestType.Replacement, mice[9], "Mouse left button double-clicks, please replace.", 110, 6, false),
            (active[7], RequestType.Repair, laptops[7], "Laptop fan is very loud under load.", 2, 0, false),
            (active[10], RequestType.NewAsset, null, "Need a USB-C dock for the new laptop.", 50, 4, false),
            (active[0], RequestType.Other, null, "Please update my laptop to the latest OS image.", 130, 6, false)
        };
        var number = 0;
        var users = await db.Users.ToDictionaryAsync(u => u.EmployeeId ?? 0);
        foreach (var r in requests)
        {
            number++;
            var created = _now.AddDays(-r.DaysAgo).AddHours(-_random.Next(1, 8));
            var requesterUser = users.GetValueOrDefault(r.Who.Id) ?? _admin;
            var request = new AssetRequest
            {
                RequestNumber = $"REQ-{number:D4}", Type = r.Type, Asset = r.Asset, RequesterEmployee = r.Who, RequesterUser = requesterUser,
                Comments = r.Comments, Status = RequestStatus.Submitted, CreatedAt = created, UpdatedAt = created
            };
            request.StatusHistory.Add(new RequestStatusHistory { ToStatus = RequestStatus.Submitted, ChangedBy = requesterUser, ChangedAt = created, Note = "Request submitted" });
            var at = created;
            if (r.Rejected)
            {
                at = at.AddHours(5);
                request.StatusHistory.Add(new RequestStatusHistory { FromStatus = RequestStatus.Submitted, ToStatus = RequestStatus.Rejected, ChangedBy = _admin, ChangedAt = at, Note = "Not covered by the equipment policy. Please use the ergonomic allowance." });
                request.Status = RequestStatus.Rejected;
            }
            for (var s = 0; s < r.Steps; s++)
            {
                at = at.AddHours(_random.Next(3, 30));
                if (at > _now) at = _now.AddMinutes(-s - 1);
                var to = flow[s];
                var by = to is RequestStatus.Checking or RequestStatus.Received ? requesterUser : _admin;
                request.StatusHistory.Add(new RequestStatusHistory { FromStatus = request.Status, ToStatus = to, ChangedBy = by, ChangedAt = at });
                request.Status = to;
            }
            request.UpdatedAt = at;
            db.Requests.Add(request);
            if (r.Type == RequestType.Repair && r.Asset is not null)
                History(r.Asset, AssetAction.RepairRequested, r.Asset.Status, r.Asset.Status, r.Who.Id, $"{request.RequestNumber}: {r.Comments}", created);
        }

        // ---- A few sign-in events ----
        foreach (var (u, days) in new[] { (_admin, 1), (users[active[0].Id], 2), (users[active[2].Id], 3), (_admin, 5) })
            Activity("Login", "Signed in", "User", u.Id, _now.AddDays(-days).AddHours(-2), u);

        await db.SaveChangesAsync();
        logger.LogInformation("Demo data seeded: {Employees} employees, {Assets} assets", employees.Count + 1, await db.Assets.CountAsync());
    }

    // ---------- helpers ----------

    private static Dictionary<string, T> Lookups<T>(params string[] names) where T : LookupEntity, new() =>
        names.ToDictionary(n => n, n => new T { Name = n });

    private Employee NewEmployee(string code, string name, string email, Department dept, Designation title, int daysAgo) => new()
    {
        EmployeeCode = code, FullName = name, Email = email, NormalizedEmail = email.ToUpperInvariant(),
        ContactNumber = $"+1 555 {_random.Next(100, 999)} {_random.Next(1000, 9999)}", CompanyName = "AssetPro Demo Co.",
        Department = dept, Designation = title, Status = EmployeeStatus.Active,
        CreatedAt = _now.AddDays(-daysAgo), UpdatedAt = _now.AddDays(-daysAgo)
    };

    private void AddUser(Employee employee, string[] permissions)
    {
        var user = new User
        {
            Email = employee.Email, NormalizedEmail = employee.NormalizedEmail, Role = UserRole.User, EmployeeId = employee.Id,
            Permissions = permissions.Select(p => new UserPermission { Permission = p }).ToList()
        };
        user.PasswordHash = hasher.HashPassword(user, DemoUserPassword);
        db.Users.Add(user);
    }

    private AssetAssignment Assign(Asset asset, Employee employee, int daysAgo)
    {
        var at = _now.AddDays(-daysAgo).AddHours(-_random.Next(1, 6));
        var assignment = new AssetAssignment
        {
            Asset = asset, Employee = employee, AssignedById = _admin.Id, AssignedAt = at, ConditionAtAssignment = asset.Condition,
            Notes = "Issued at onboarding"
        };
        db.AssetAssignments.Add(assignment);
        asset.Status = AssetStatus.Assigned;
        History(asset, AssetAction.Assigned, AssetStatus.InStock, AssetStatus.Assigned, employee.Id, $"Assigned to {employee.FullName}", at);
        var item = asset.InventoryItem!;
        var inStock = db.Assets.Local.Count(a => a.InventoryItem == item && a.Status == AssetStatus.InStock);
        Txn(item, InventoryTransactionType.StockOut, 1, inStock, asset, employee, $"Assigned to {employee.FullName}", at);
        Activity("AssetAssigned", $"Assigned {asset.AssetTag} to {employee.FullName}", "Asset", asset.Id, at);
        return assignment;
    }

    private void Return(AssetAssignment assignment, Asset asset, int daysAgo, AssetCondition condition, AssetStatus result, string notes, string? damage = null)
    {
        var at = _now.AddDays(-daysAgo);
        assignment.ReturnedAt = at;
        assignment.ReturnedBy = assignment.Employee?.FullName;
        assignment.ReceivedById = _admin.Id;
        assignment.ReturnCondition = condition;
        assignment.DamageDetails = damage;
        assignment.ReturnNotes = notes;
        asset.Condition = condition;
        asset.Status = result;
        History(asset, AssetAction.Returned, AssetStatus.Assigned, result, assignment.EmployeeId, notes, at);
        if (result == AssetStatus.InStock)
        {
            var inStock = db.Assets.Local.Count(a => a.InventoryItem == asset.InventoryItem && a.Status == AssetStatus.InStock);
            Txn(asset.InventoryItem!, InventoryTransactionType.Return, 1, inStock, asset, assignment.Employee, notes, at);
        }
        Activity("AssetReturned", $"{asset.AssetTag} returned ({result})", "Asset", asset.Id, at);
    }

    private void Scrap(Asset asset, int daysAgo, string reason)
    {
        var at = _now.AddDays(-daysAgo);
        var previous = asset.Status;
        asset.Status = AssetStatus.Scrapped;
        db.ScrapRecords.Add(new ScrapRecord { AssetId = asset.Id, ScrapDate = DateOnly.FromDateTime(at), Reason = reason, Quantity = 1, PerformedById = _admin.Id, CreatedAt = at });
        History(asset, AssetAction.Scrapped, previous, AssetStatus.Scrapped, null, reason, at);
        var inStock = db.Assets.Local.Count(a => a.InventoryItem == asset.InventoryItem && a.Status == AssetStatus.InStock);
        Txn(asset.InventoryItem!, InventoryTransactionType.Scrap, 1, inStock, asset, null, reason, at);
        Activity("AssetScrapped", $"{asset.AssetTag} moved to scrap: {reason}", "Asset", asset.Id, at);
    }

    private void StatusChange(Asset asset, AssetStatus status, AssetAction action, int daysAgo, string notes)
    {
        var at = _now.AddDays(-daysAgo);
        var previous = asset.Status;
        asset.Status = status;
        History(asset, action, previous, status, null, notes, at);
        var inStock = db.Assets.Local.Count(a => a.InventoryItem == asset.InventoryItem && a.Status == AssetStatus.InStock);
        Txn(asset.InventoryItem!, InventoryTransactionType.Adjustment, -1, inStock, asset, null, notes, at);
        Activity("AssetStatusChanged", $"{asset.AssetTag}: {previous} → {status}", "Asset", asset.Id, at);
    }

    private void Repair(Asset asset, Employee holder, int daysAgo, bool start, string notes)
    {
        var at = _now.AddDays(-daysAgo);
        if (start)
        {
            asset.Status = AssetStatus.UnderRepair;
            History(asset, AssetAction.RepairStarted, AssetStatus.Assigned, AssetStatus.UnderRepair, holder.Id, notes, at);
        }
        else
        {
            asset.Status = AssetStatus.Assigned;
            asset.Condition = AssetCondition.Good;
            History(asset, AssetAction.RepairCompleted, AssetStatus.UnderRepair, AssetStatus.Assigned, holder.Id, notes, at);
        }
        Activity("AssetRepaired", $"Repair {(start ? "started" : "completed")} for {asset.AssetTag}", "Asset", asset.Id, at);
    }

    private void History(Asset asset, AssetAction action, AssetStatus? previous, AssetStatus? next, int? employeeId, string notes, DateTime at) =>
        db.AssetHistory.Add(new AssetHistory
        {
            Asset = asset, Action = action, PreviousStatus = previous, NewStatus = next, EmployeeId = employeeId,
            PerformedById = _admin.Id, Notes = notes, OccurredAt = at
        });

    private void Txn(InventoryItem item, InventoryTransactionType type, int qty, int balance, Asset? asset, Employee? employee, string notes, DateTime at) =>
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            InventoryItem = item, Type = type, Quantity = qty, BalanceAfter = balance, Asset = asset, Employee = employee,
            Notes = notes, PerformedById = _admin.Id, OccurredAt = at
        });

    private void Activity(string action, string description, string entity, int id, DateTime at, User? user = null) =>
        db.ActivityLogs.Add(new ActivityLog
        {
            OccurredAt = at, UserId = (user ?? _admin).Id, UserEmail = (user ?? _admin).Email, Action = action, Entity = entity,
            EntityId = id.ToString(), Description = description, IpAddress = "127.0.0.1", Result = ActivityResult.Success
        });
}
