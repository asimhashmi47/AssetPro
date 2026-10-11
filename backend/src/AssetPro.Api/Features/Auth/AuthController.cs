using System.ComponentModel.DataAnnotations;
using AssetPro.Api.Auth;
using AssetPro.Api.Common;
using AssetPro.Api.Data;
using AssetPro.Api.Domain;
using AssetPro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AssetPro.Api.Features.Auth;

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = "";

    [Required, StringLength(128)]
    public string Password { get; set; } = "";

    public bool RememberMe { get; set; }
}

public sealed class ChangePasswordRequest
{
    [Required, StringLength(128)]
    public string CurrentPassword { get; set; } = "";

    [Required, StringLength(128, MinimumLength = 8)]
    public string NewPassword { get; set; } = "";
}

public sealed record SessionUser(int Id, string Email, string FullName, string Role, int? EmployeeId, IReadOnlyList<string> Permissions);

public sealed record LoginResponse(string Token, DateTime ExpiresAt, SessionUser User);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext db,
    JwtTokenService tokens,
    IPasswordHasher<User> hasher,
    ActivityLogger activity,
    ICurrentUser currentUser) : ControllerBase
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var normalized = request.Email.Trim().ToUpperInvariant();
        var user = await db.Users.Include(u => u.Permissions).Include(u => u.Employee)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized);

        // Same message for unknown user and wrong password, so accounts cannot be enumerated.
        const string invalid = "Invalid email or password.";

        if (user is null)
        {
            await activity.SaveNowAsync(ActivityActions.LoginFailed, "Sign-in failed: unknown account", "User", null,
                ActivityResult.Failure, userEmail: request.Email.Trim());
            throw new UnauthorizedException(invalid);
        }

        if (user.LockoutEndsAt > DateTime.UtcNow)
            throw new UnauthorizedException("Too many failed attempts. Try again in a few minutes.");

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutEndsAt = DateTime.UtcNow.Add(LockoutDuration);
                user.FailedLoginCount = 0;
            }
            await activity.SaveNowAsync(ActivityActions.LoginFailed, "Sign-in failed: wrong password", "User", user.Id,
                ActivityResult.Failure, user.Id, user.Email);
            throw new UnauthorizedException(invalid);
        }

        if (!user.IsActive)
            throw new UnauthorizedException("This account is disabled. Contact your administrator.");

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, request.Password);

        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
        user.LastLoginAt = DateTime.UtcNow;

        var permissions = Permissions.Effective(user.Role, user.Permissions.Select(p => p.Permission));
        var (token, expires) = tokens.Create(user, permissions, request.RememberMe);
        activity.Add(ActivityActions.Login, "Signed in", "User", user.Id, userId: user.Id, userEmail: user.Email);
        await db.SaveChangesAsync();

        return Ok(new LoginResponse(token, expires, ToSessionUser(user, permissions)));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await activity.SaveNowAsync(ActivityActions.Logout, "Signed out", "User", currentUser.UserId);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<SessionUser>> Me()
    {
        var user = await db.Users.AsNoTracking().Include(u => u.Permissions).Include(u => u.Employee)
                       .FirstOrDefaultAsync(u => u.Id == currentUser.UserId)
                   ?? throw new UnauthorizedException("Session is no longer valid.");
        return Ok(ToSessionUser(user, Permissions.Effective(user.Role, user.Permissions.Select(p => p.Permission))));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await db.Users.FirstAsync(u => u.Id == currentUser.UserId);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            throw ValidationFailedException.For("currentPassword", "Current password is incorrect.");
        PasswordRules.Validate(request.NewPassword, "newPassword");

        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        activity.Add(ActivityActions.UserUpdated, "Changed own password", "User", user.Id);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static SessionUser ToSessionUser(User user, IReadOnlyList<string> permissions) =>
        new(user.Id, user.Email, user.Employee?.FullName ?? user.Email, user.Role.ToString(), user.EmployeeId, permissions);
}

public static class PasswordRules
{
    /// <summary>At least 8 characters with a letter and a digit. Kept simple on purpose for the MVP.</summary>
    public static void Validate(string password, string field)
    {
        if (password.Length < 8 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw ValidationFailedException.For(field, "Password must be at least 8 characters and contain a letter and a number.");
    }
}
