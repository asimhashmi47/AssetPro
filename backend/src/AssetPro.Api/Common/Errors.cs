namespace AssetPro.Api.Common;

/// <summary>Base type for expected, user-facing failures. The message is safe to show to the client.</summary>
public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class NotFoundException(string message) : AppException(message, StatusCodes.Status404NotFound);

/// <summary>A request that is well-formed but breaks a business rule (e.g. assigning a scrapped asset).</summary>
public sealed class BusinessRuleException(string message) : AppException(message, StatusCodes.Status409Conflict);

public sealed class ValidationFailedException(string message, IDictionary<string, string[]>? errors = null)
    : AppException(message, StatusCodes.Status400BadRequest)
{
    public IDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();

    public static ValidationFailedException For(string field, string message) =>
        new(message, new Dictionary<string, string[]> { [field] = [message] });
}

public sealed class ForbiddenException(string message = "You do not have permission to perform this action.")
    : AppException(message, StatusCodes.Status403Forbidden);

public sealed class UnauthorizedException(string message) : AppException(message, StatusCodes.Status401Unauthorized);

/// <summary>Uniform error body returned by every failing endpoint.</summary>
public sealed record ApiError(int Status, string Title, string Message, string? TraceId, IDictionary<string, string[]>? Errors = null);
