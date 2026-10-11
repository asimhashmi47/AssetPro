using System.ComponentModel.DataAnnotations;
using AssetPro.Api.Auth;
using AssetPro.Api.Common;
using AssetPro.Api.Data;
using AssetPro.Api.Domain;
using AssetPro.Api.Features.Assets;
using AssetPro.Api.Features.Auth;
using AssetPro.Api.Features.Requests;
using AssetPro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetPro.Api.Features.Employees;

public sealed class EmployeeListDto
{
    public int Id { get; init; }
    public string EmployeeCode { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Email { get; init; } = "";
    public string? ContactNumber { get; init; }
    public string? CompanyName { get; init; }
    public int? DepartmentId { get; init; }
    public string? Department { get; init; }
    public int? DesignationId { get; init; }
    public string? Designation { get; init; }
    public UserRole? Role { get; init; }
    public int? UserId { get; init; }
    public EmployeeStatus Status { get; init; }
    public int AssignedAssets { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class EmployeeProfileDto
{
    public required EmployeeListDto Employee { get; init; }
    public IReadOnlyList<AssetListDto> Assets { get; init; } = [];
    public IReadOnlyList<AssetHistoryDto> History { get; init; } = [];
    public IReadOnlyList<RequestListDto> Requests { get; init; } = [];
}

public sealed class SaveEmployeeRequest
{
    [Required, StringLength(150, MinimumLength = 2)]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = "";

    [StringLength(30), RegularExpression(@"^[0-9+()\-\s]*$", ErrorMessage = "Contact number may contain digits, spaces and + ( ) -.")]
    public string? ContactNumber { get; set; }

    [StringLength(150)]
    public string? CompanyName { get; set; }

    public int? DepartmentId { get; set; }
    public int? DesignationId { get; set; }

    [EnumDataType(typeof(EmployeeStatus))]
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    /// <summary>Optional: also create a sign-in account (requires Users.Manage).</summary>
    public CreateLoginRequest? Login { get; set; }
}

public sealed class CreateLoginRequest
{
    [EnumDataType(typeof(UserRole))]
    public UserRole Role { get; set; } = UserRole.User;

    [Required, StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = "";
}

[ApiController]
[Authorize]
[Route("api/employees")]
public sealed class EmployeesController(
    AppDbContext db,
    ICurrentUser currentUser,
    ActivityLogger activity,
    IPasswordHasher<User> hasher,
    RecordDeletionService deletion) : ControllerBase
{
    private IQueryable<EmployeeListDto> Query() => Projection(db);

    internal static IQueryable<EmployeeListDto> Projection(AppDbContext db) => db.Employees.AsNoTracking().Select(e => new EmployeeListDto
    {
        Id = e.Id,
        EmployeeCode = e.EmployeeCode,
        FullName = e.FullName,
        Email = e.Email,
        ContactNumber = e.ContactNumber,
        CompanyName = e.CompanyName,
        DepartmentId = e.DepartmentId,
        Department = e.Department != null ? e.Department.Name : null,
        DesignationId = e.DesignationId,
        Designation = e.Designation != null ? e.Designation.Name : null,
        Role = e.User != null ? e.User.Role : null,
        UserId = e.User != null ? e.User.Id : null,
        Status = e.Status,
        AssignedAssets = db.AssetAssignments.Count(x => x.EmployeeId == e.Id && x.ReturnedAt == null),
        CreatedAt = e.CreatedAt
    });

    [HttpGet]
    [Authorize(Policy = Permissions.EmployeesView)]
    public Task<PagedResult<EmployeeListDto>> List([FromQuery] GridQuery grid, CancellationToken ct) =>
        Query().ToPagedResultAsync(grid, nameof(EmployeeListDto.FullName), ct: ct);

    /// <summary>Lightweight list for pickers (assign / stock-out dialogs).</summary>
    [HttpGet("options")]
    public async Task<IActionResult> Options([FromQuery] string? search, CancellationToken ct)
    {
        if (!currentUser.Has(Permissions.EmployeesView) && !currentUser.Has(Permissions.AssetsAssign) && !currentUser.Has(Permissions.InventoryStockOut))
            throw new ForbiddenException();
        var query = db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(e => EF.Functions.Like(e.FullName, term) || EF.Functions.Like(e.EmployeeCode, term) || EF.Functions.Like(e.Email, term));
        }
        var items = await query.OrderBy(e => e.FullName).Take(50)
            .Select(e => new { e.Id, e.FullName, e.EmployeeCode, Department = e.Department != null ? e.Department.Name : null })
            .ToListAsync(ct);
        return Ok(items);
    }

    /// <summary>Full profile. Employees.View holders can open anyone; others only themselves (IDOR protection).</summary>
    [HttpGet("{id:int}")]
    public async Task<EmployeeProfileDto> Get(int id, CancellationToken ct)
    {
        if (!currentUser.Has(Permissions.EmployeesView) && currentUser.EmployeeId != id)
            throw new NotFoundException("Employee not found.");
        return await LoadProfileAsync(db, Query(), id, ct);
    }

    internal static async Task<EmployeeProfileDto> LoadProfileAsync(AppDbContext db, IQueryable<EmployeeListDto> employees, int id, CancellationToken ct)
    {
        var employee = await employees.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Employee not found.");

        var assetIds = db.AssetAssignments.Where(x => x.EmployeeId == id && x.ReturnedAt == null).Select(x => x.AssetId);
        var assets = await db.Assets.AsNoTracking().Where(a => assetIds.Contains(a.Id))
            .Select(AssetService.ToListDto).OrderByDescending(a => a.AssignedAt).ToListAsync(ct);

        var history = await db.AssetHistory.AsNoTracking().Where(h => h.EmployeeId == id)
            .OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.Id).Take(100)
            .Select(AssetService.ToHistoryDto).ToListAsync(ct);

        var requests = await db.Requests.AsNoTracking().Where(r => r.RequesterEmployeeId == id)
            .Select(RequestService.ToListDto).OrderByDescending(r => r.CreatedAt).Take(50).ToListAsync(ct);

        return new EmployeeProfileDto { Employee = employee, Assets = assets, History = history, Requests = requests };
    }

    [HttpPost]
    [Authorize(Policy = Permissions.EmployeesCreate)]
    public async Task<ActionResult<EmployeeListDto>> Create(SaveEmployeeRequest request)
    {
        await ValidateAsync(request, null);
        if (request.Login is not null && !currentUser.Has(Permissions.UsersManage))
            throw new ForbiddenException("You need the Users.Manage permission to create sign-in accounts.");
        if (request.Login is { Role: UserRole.Admin } && !currentUser.IsAdmin)
            throw new ForbiddenException("Only administrators can create administrator accounts.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var employee = new Employee { EmployeeCode = await NextCodeAsync() };
        Apply(employee, request);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        if (request.Login is { } login)
        {
            PasswordRules.Validate(login.Password, "login.password");
            var user = new User
            {
                Email = employee.Email,
                NormalizedEmail = employee.NormalizedEmail,
                Role = login.Role,
                EmployeeId = employee.Id,
                IsActive = employee.Status == EmployeeStatus.Active
            };
            user.PasswordHash = hasher.HashPassword(user, login.Password);
            db.Users.Add(user);
            activity.Add(ActivityActions.UserCreated, $"Created {login.Role} account for {employee.FullName}", "User", null);
        }

        activity.Add(ActivityActions.EmployeeCreated, $"Created employee {employee.FullName} ({employee.EmployeeCode})", "Employee", employee.Id);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        var dto = await Query().FirstAsync(e => e.Id == employee.Id);
        return CreatedAtAction(nameof(Get), new { id = employee.Id }, dto);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.EmployeesEdit)]
    public async Task<EmployeeListDto> Update(int id, SaveEmployeeRequest request)
    {
        var employee = await db.Employees.Include(e => e.User).FirstOrDefaultAsync(e => e.Id == id)
                       ?? throw new NotFoundException("Employee not found.");
        await ValidateAsync(request, id);

        if (request.Status == EmployeeStatus.Inactive && employee.Status == EmployeeStatus.Active)
        {
            var open = await db.AssetAssignments.CountAsync(x => x.EmployeeId == id && x.ReturnedAt == null);
            if (open > 0)
                throw new BusinessRuleException($"{employee.FullName} still holds {open} asset(s). Process the returns before deactivating.");
            if (employee.User is { } linked)
            {
                if (linked.Id == currentUser.UserId) throw new BusinessRuleException("You cannot deactivate yourself.");
                linked.IsActive = false;
                linked.SecurityStamp = Guid.NewGuid().ToString("N");
            }
        }

        var emailChanged = !string.Equals(employee.Email, request.Email.Trim(), StringComparison.OrdinalIgnoreCase);
        // The employee email is also the sign-in email; don't let non-admins redirect an administrator's account.
        if (emailChanged && employee.User is { Role: UserRole.Admin } && !currentUser.IsAdmin)
            throw new ForbiddenException("Only administrators can change an administrator's email.");
        Apply(employee, request);
        if (emailChanged && employee.User is { } user)
        {
            if (await db.Users.AnyAsync(u => u.NormalizedEmail == employee.NormalizedEmail && u.Id != user.Id))
                throw ValidationFailedException.For("email", "Another account already uses this email.");
            user.Email = employee.Email;
            user.NormalizedEmail = employee.NormalizedEmail;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
        }

        activity.Add(ActivityActions.EmployeeUpdated, $"Updated employee {employee.FullName}", "Employee", employee.Id);
        await db.SaveChangesAsync();
        return await Query().FirstAsync(e => e.Id == id);
    }

    private async Task ValidateAsync(SaveEmployeeRequest request, int? exceptId)
    {
        var normalized = request.Email.Trim().ToUpperInvariant();
        if (await db.Employees.AnyAsync(e => e.NormalizedEmail == normalized && e.Id != exceptId))
            throw ValidationFailedException.For("email", "An employee with this email already exists.");
        if (exceptId is null && request.Login is not null && await db.Users.AnyAsync(u => u.NormalizedEmail == normalized))
            throw ValidationFailedException.For("email", "A sign-in account with this email already exists.");
        if (request.DepartmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d))
            throw ValidationFailedException.For("departmentId", "Select a valid department.");
        if (request.DesignationId is { } g && !await db.Designations.AnyAsync(x => x.Id == g))
            throw ValidationFailedException.For("designationId", "Select a valid designation.");
    }

    private static void Apply(Employee employee, SaveEmployeeRequest request)
    {
        employee.FullName = request.FullName.Trim();
        employee.Email = request.Email.Trim();
        employee.NormalizedEmail = employee.Email.ToUpperInvariant();
        employee.ContactNumber = AssetService.Clean(request.ContactNumber);
        employee.CompanyName = AssetService.Clean(request.CompanyName);
        employee.DepartmentId = request.DepartmentId;
        employee.DesignationId = request.DesignationId;
        employee.Status = request.Status;
    }

    private async Task<string> NextCodeAsync()
    {
        var codes = await db.Employees.Where(e => e.EmployeeCode.StartsWith("EMP-")).Select(e => e.EmployeeCode).ToListAsync();
        var max = codes.Select(c => int.TryParse(c[4..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return $"EMP-{max + 1:D4}";
    }

    /// <summary>Permanently removes the employee, their sign-in and their requests. Administrators only.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await deletion.DeleteEmployeeAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Self-service endpoints: always scoped to the signed-in user's own employee record.</summary>
[ApiController]
[Authorize]
[Route("api/me")]
public sealed class MeController(AppDbContext db, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<EmployeeProfileDto> Profile(CancellationToken ct)
    {
        var employeeId = currentUser.EmployeeId ?? throw new NotFoundException("Your account is not linked to an employee profile.");
        return await EmployeesController.LoadProfileAsync(db, EmployeesController.Projection(db), employeeId, ct);
    }
}
