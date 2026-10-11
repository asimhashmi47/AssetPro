using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace AssetPro.Api.Common;

/// <summary>
/// Paging, sorting, global search and per-column filters for the advanced tables.
/// Query string shape: ?page=1&amp;pageSize=10&amp;search=dell&amp;sort=name&amp;dir=asc&amp;filters[status]=InStock,Assigned&amp;filters[purchaseDate]=2026-01-01..2026-06-30
/// </summary>
public sealed class GridQuery
{
    public const int MaxPageSize = 5000;

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Search { get; set; }
    public string? Sort { get; set; }
    public string? Dir { get; set; }
    public Dictionary<string, string> Filters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Descending => string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public static class GridQueryExtensions
{
    private static readonly MethodInfo LikeMethod = typeof(DbFunctionsExtensions).GetMethod(
        nameof(DbFunctionsExtensions.Like), [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

    /// <summary>
    /// Applies the grid query to a projected DTO query. Filters/sorts are matched to public DTO properties
    /// by name (case-insensitive); unknown keys are ignored so clients cannot reach arbitrary members.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> source, GridQuery grid, string defaultSort, bool defaultDescending = false,
        CancellationToken ct = default)
    {
        var query = source.ApplyFilters(grid).ApplySearch(grid.Search).ApplySort(grid, defaultSort, defaultDescending);

        var pageSize = Math.Clamp(grid.PageSize, 1, GridQuery.MaxPageSize);
        var page = Math.Max(1, grid.Page);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<T>(items, total, page, pageSize);
    }

    public static IQueryable<T> ApplyFilters<T>(this IQueryable<T> query, GridQuery grid)
    {
        foreach (var (key, rawValue) in grid.Filters)
        {
            if (string.IsNullOrWhiteSpace(rawValue)) continue;
            var prop = FindProperty<T>(key);
            if (prop is null) continue;

            var parameter = Expression.Parameter(typeof(T), "x");
            var member = Expression.Property(parameter, prop);
            var predicate = BuildFilter(member, prop.PropertyType, rawValue.Trim());
            if (predicate is not null)
                query = query.Where(Expression.Lambda<Func<T, bool>>(predicate, parameter));
        }
        return query;
    }

    public static IQueryable<T> ApplySearch<T>(this IQueryable<T> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return query;
        var parameter = Expression.Parameter(typeof(T), "x");
        Expression? body = null;
        foreach (var prop in typeof(T).GetProperties().Where(p => p.PropertyType == typeof(string)))
        {
            var like = LikeExpression(Expression.Property(parameter, prop), search.Trim());
            body = body is null ? like : Expression.OrElse(body, like);
        }
        return body is null ? query : query.Where(Expression.Lambda<Func<T, bool>>(body, parameter));
    }

    public static IQueryable<T> ApplySort<T>(this IQueryable<T> query, GridQuery grid, string defaultSort, bool defaultDescending)
    {
        var prop = (string.IsNullOrWhiteSpace(grid.Sort) ? null : FindProperty<T>(grid.Sort)) ?? FindProperty<T>(defaultSort)
                   ?? throw new InvalidOperationException($"Default sort '{defaultSort}' is not a property of {typeof(T).Name}.");
        var descending = string.IsNullOrWhiteSpace(grid.Sort) || FindProperty<T>(grid.Sort) is null ? defaultDescending : grid.Descending;

        var parameter = Expression.Parameter(typeof(T), "x");
        var lambda = Expression.Lambda(Expression.Property(parameter, prop), parameter);
        var method = descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy);
        var call = Expression.Call(typeof(Queryable), method, [typeof(T), prop.PropertyType], query.Expression, Expression.Quote(lambda));
        return query.Provider.CreateQuery<T>(call);
    }

    private static PropertyInfo? FindProperty<T>(string name) =>
        typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

    private static Expression? BuildFilter(MemberExpression member, Type propertyType, string value)
    {
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (type == typeof(string))
            return LikeExpression(member, value);

        if (type.IsEnum)
        {
            // Multi-select: "InStock,Assigned" => x.Status == InStock || x.Status == Assigned
            Expression? any = null;
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Enum.TryParse(type, part, ignoreCase: true, out var parsed)) continue;
                var eq = Expression.Equal(member, Param(parsed, propertyType));
                any = any is null ? eq : Expression.OrElse(any, eq);
            }
            return any ?? Expression.Constant(false);
        }

        if (type == typeof(bool))
            return bool.TryParse(value, out var b) ? Expression.Equal(member, Param(b, propertyType)) : null;

        // Ranges use "from..to" (either side optional); a single value means equality.
        var (fromText, toText, isRange) = SplitRange(value);

        if (type == typeof(DateTime))
        {
            Expression? result = null;
            if (TryDate(fromText, out var from))
                result = Expression.GreaterThanOrEqual(member, Param((DateTime?)DateTime.SpecifyKind(from, DateTimeKind.Utc), typeof(DateTime?)).ConvertTo(propertyType));
            var upperText = isRange ? toText : fromText;
            if (TryDate(upperText, out var to))
            {
                var upper = Expression.LessThan(member, Param((DateTime?)DateTime.SpecifyKind(to.AddDays(1), DateTimeKind.Utc), typeof(DateTime?)).ConvertTo(propertyType));
                result = result is null ? upper : Expression.AndAlso(result, upper);
            }
            return result;
        }

        if (type == typeof(DateOnly))
        {
            Expression? result = null;
            if (TryDate(fromText, out var from))
                result = Expression.GreaterThanOrEqual(member, Param((DateOnly?)DateOnly.FromDateTime(from), typeof(DateOnly?)).ConvertTo(propertyType));
            var upperText = isRange ? toText : fromText;
            if (TryDate(upperText, out var to))
            {
                var upper = Expression.LessThanOrEqual(member, Param((DateOnly?)DateOnly.FromDateTime(to), typeof(DateOnly?)).ConvertTo(propertyType));
                result = result is null ? upper : Expression.AndAlso(result, upper);
            }
            return result;
        }

        if (type == typeof(int) || type == typeof(decimal) || type == typeof(double))
        {
            Expression? result = null;
            if (TryNumber(fromText, type, out var from))
                result = isRange
                    ? Expression.GreaterThanOrEqual(member, Param(from, propertyType))
                    : Expression.Equal(member, Param(from, propertyType));
            if (isRange && TryNumber(toText, type, out var to))
            {
                var upper = Expression.LessThanOrEqual(member, Param(to, propertyType));
                result = result is null ? upper : Expression.AndAlso(result, upper);
            }
            return result;
        }

        return null;
    }

    /// <summary>Wraps a value so EF Core sends it as a SQL parameter instead of an inlined literal.</summary>
    private static Expression Param(object? value, Type type)
    {
        var holder = Activator.CreateInstance(typeof(ValueHolder<>).MakeGenericType(type), value)!;
        return Expression.Property(Expression.Constant(holder), "Value");
    }

    private sealed class ValueHolder<TValue>(TValue value)
    {
        public TValue Value { get; } = value;
    }

    private static Expression ConvertTo(this Expression expression, Type type) =>
        expression.Type == type ? expression : Expression.Convert(expression, type);

    private static (string From, string To, bool IsRange) SplitRange(string value)
    {
        var index = value.IndexOf("..", StringComparison.Ordinal);
        return index < 0 ? (value, value, false) : (value[..index].Trim(), value[(index + 2)..].Trim(), true);
    }

    private static bool TryDate(string text, out DateTime value) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    private static bool TryNumber(string text, Type type, out object? value)
    {
        value = null;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)) return false;
        value = Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
        return true;
    }

    private static Expression LikeExpression(Expression member, string term)
    {
        var escaped = term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return Expression.Call(LikeMethod,
            Expression.Constant(EF.Functions),
            member,
            Param($"%{escaped}%", typeof(string)),
            Expression.Constant("\\"));
    }
}
