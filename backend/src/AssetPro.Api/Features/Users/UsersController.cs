using System.ComponentModel.DataAnnotations;
using AssetPro.Api.Auth;
using AssetPro.Api.Common;
using AssetPro.Api.Data;
using AssetPro.Api.Domain;
using AssetPro.Api.Features.Auth;
using AssetPro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetPro.Api.Features.Users;

public sealed class UserListDto
{
    public int Id { get; init; }
    public string Email { get; init; } = "";
    public string? FullName { get; init; }
    public int? EmployeeId { get; init; }
    public string? EmployeeCode { get; init; }
    public UserRole Role { get; init; }
    public bool IsActive { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<string> GrantedPermissions { get; init; } = [];
}

public sealed class CreateUserRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Employee is required.")]
    public int EmployeeId { get; set; }

    [EnumDataType(typeof(UserRole))]
    public UserRole Role { get; set; } = UserRole.User;

    [Required, StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = "";

    public List<string> Permissions { get; set; } = [];
}

public sealed class UpdateUserRequest
{
    [EnumDataType(typeof(UserRole))]
    public UserRole Role { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Extra permissions for standard users; ignored for administrators (who have everything).</summary>
    public List<string> Permissions { get; set; } = [];
}

public sealed class ResetPasswordRequest
{
    [Required, StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = "";
}

[ApiController]
[Authorize(Policy = Permissions.UsersManage)]
[Route("api/users")]
public sealed class UsersController(AppDbContext db, ICurrentUser currentUser, ActivityLogger activity, IPasswordHasher<User> hasher, RecordDeletionService deletion) : ControllerBase
{
    private IQueryable<UserListDto> Query() => db.Users.AsNoTracking().Select(u => new UserListDto
    {
        Id = u.Id,
        Email = u.Email,
        FullName = u.Employee != null ? u.Employee.FullName : null,
        EmployeeId = u.EmployeeId,
        EmployeeCode = u.Employee != null ? u.Employee.EmployeeCode : null,
        Role = u.Role,
        IsActive = u.IsActive,
        LastLoginAt = u.LastLoginAt,
        CreatedAt = u.CreatedAt,
        GrantedPermissions = u.Permissions.Select(p => p.Permission).ToList()
    });

    [HttpGet]
    public Task<PagedResult<UserListDto>> List([FromQuery] GridQuery grid, CancellationToken ct) =>
        Query().ToPagedResultAsync(grid, nameof(UserListDto.Email), ct: ct);

    [HttpGet("permissions")]
    public IActionResult PermissionCatalog() =>
        Ok(new { all = Permissions.All, userDefaults = Permissions.UserDefaults });

    [HttpPost]
    public async Task<ActionResult<UserListDto>> Create(CreateUserRequest request)
    {
        var employee = await db.Employees.Include(e => e.User).FirstOrDefaultAsync(e => e.Id == request.EmployeeId)
                       ?? throw ValidationFailedException.For("employeeId", "Select a valid employee.");
        if (employee.User is not null) throw new BusinessRuleException($"{employee.FullName} already has a sign-in account.");
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == employee.NormalizedEmail))
            throw ValidationFailedException.For("employeeId", "An account with this employee's email already exists.");
        PasswordRules.Validate(request.Password, "password");
        if (request.Role == UserRole.Admin && !currentUser.IsAdmin)
            throw new ForbiddenException("Only administrators can create administrator accounts.");

        var user = new User
        {
            Email = employee.Email,
            NormalizedEmail = employee.NormalizedEmail,
            Role = request.Role,
            EmployeeId = employee.Id,
            IsActive = employee.Status == EmployeeStatus.Active,
            Permissions = ValidPermissions(request.Role, request.Permissions).Select(p => new UserPermission { Permission = p }).ToList()
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        activity.Add(ActivityActions.UserCreated, $"Created {user.Role} account {user.Email}", "User", user.Id);
        await db.SaveChangesAsync();
        return Ok(await Query().FirstAsync(u => u.Id == user.Id));
    }

    [HttpPut("{id:int}")]
    public async Task<UserListDto> Update(int id, UpdateUserRequest request)
    {
        var user = await db.Users.Include(u => u.Permissions).FirstOrDefaultAsync(u => u.Id == id)
                   ?? throw new NotFoundException("User not found.");

        if (id == currentUser.UserId && (request.Role != user.Role || !request.IsActive))
            throw new BusinessRuleException("You cannot change your own role or deactivate your own account.");
        EnsureMayManage(user);
        if (request.Role == UserRole.Admin && !currentUser.IsAdmin)
            throw new ForbiddenException("Only administrators can grant the administrator role.");

        var losingAdmin = user.Role == UserRole.Admin && user.IsActive && (request.Role != UserRole.Admin || !request.IsActive);
        if (losingAdmin && await db.Users.CountAsync(u => u.Role == UserRole.Admin && u.IsActive) <= 1)
            throw new BusinessRuleException("At least one active administrator is required.");

        user.Role = request.Role;
        user.IsActive = request.IsActive;
        db.UserPermissions.RemoveRange(user.Permissions);
        user.Permissions = ValidPermissions(request.Role, request.Permissions).Select(p => new UserPermission { Permission = p }).ToList();
        // Force re-login so the new role/permissions take effect immediately.
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        activity.Add(ActivityActions.UserUpdated,
            $"Updated {user.Email}: role {user.Role}, {(user.IsActive ? "active" : "inactive")}, {user.Permissions.Count} extra permission(s)", "User", user.Id);
        await db.SaveChangesAsync();
        return await Query().FirstAsync(u => u.Id == id);
    }

    /// <summary>Permanently removes the record. Administrators only.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await deletion.DeleteUserAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest request)
    {
        var user = await db.Users.FindAsync(id) ?? throw new NotFoundException("User not found.");
        EnsureMayManage(user);
        PasswordRules.Validate(request.Password, "password");
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
        activity.Add(ActivityActions.UserUpdated, $"Reset password for {user.Email}", "User", user.Id);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Users.Manage can be granted to non-admins; they may manage standard users only, and never
    /// themselves - otherwise they could escalate their own permissions or take over an administrator.
    /// </summary>
    private void EnsureMayManage(User target)
    {
        if (currentUser.IsAdmin) return;
        if (target.Role == UserRole.Admin) throw new ForbiddenException("Only administrators can manage administrator accounts.");
        if (target.Id == currentUser.UserId) throw new ForbiddenException("Ask an administrator to change your own access.");
    }

    private static List<string> ValidPermissions(UserRole role, IEnumerable<string> requested)
    {
        if (role == UserRole.Admin) return [];
        var list = requested.Distinct().ToList();
        var unknown = list.Where(p => !Permissions.All.Contains(p)).ToList();
        if (unknown.Count > 0) throw ValidationFailedException.For("permissions", $"Unknown permission(s): {string.Join(", ", unknown)}");
        return list.Where(p => !Permissions.UserDefaults.Contains(p)).ToList();
    }
}
