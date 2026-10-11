using System.Security.Claims;
using System.Text;
using AssetPro.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AssetPro.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "AssetPro";
    public string Audience { get; set; } = "AssetPro.Client";
    /// <summary>HMAC signing key, at least 32 characters. Must come from configuration/secrets, never source control in production.</summary>
    public string Key { get; set; } = "";
    public int ExpiryHours { get; set; } = 8;
    public int RememberMeExpiryDays { get; set; } = 7;

    public SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(Key));
}

public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresAt) Create(User user, IReadOnlyList<string> permissions, bool rememberMe)
    {
        var expires = rememberMe
            ? DateTime.UtcNow.AddDays(_options.RememberMeExpiryDays)
            : DateTime.UtcNow.AddHours(_options.ExpiryHours);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(CurrentUser.StampClaim, user.SecurityStamp)
        };
        if (user.EmployeeId is { } employeeId) claims.Add(new Claim(CurrentUser.EmployeeClaim, employeeId.ToString()));
        claims.AddRange(permissions.Select(p => new Claim(Permissions.ClaimType, p)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = DateTime.UtcNow,
            Expires = expires,
            SigningCredentials = new SigningCredentials(_options.SigningKey(), SecurityAlgorithms.HmacSha256)
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
