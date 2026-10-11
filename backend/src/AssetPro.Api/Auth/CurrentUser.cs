using System.Security.Claims;
using AssetPro.Api.Common;
using AssetPro.Api.Domain;

namespace AssetPro.Api.Auth;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    int UserId { get; }
    string? Email { get; }
    int? EmployeeId { get; }
    bool IsAdmin { get; }
    bool Has(string permission);
    string? IpAddress { get; }
}

/// <summary>Reads the identity from the validated JWT. Never trust user/employee ids sent in request bodies.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string EmployeeClaim = "emp";
    public const string StampClaim = "sstamp";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public int UserId => int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id
        : throw new UnauthorizedException("You are not signed in.");

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public int? EmployeeId => int.TryParse(Principal?.FindFirstValue(EmployeeClaim), out var id) ? id : null;

    public bool IsAdmin => Principal?.IsInRole(nameof(UserRole.Admin)) == true;

    public bool Has(string permission) => IsAdmin || Principal?.HasClaim(Permissions.ClaimType, permission) == true;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
