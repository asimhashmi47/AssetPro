using System.Diagnostics;
using System.Threading.Channels;
using AssetPro.Api.Data;
using AssetPro.Api.Domain;

namespace AssetPro.Api.Infrastructure;

/// <summary>
/// Minimal logger provider that persists warnings/errors (and AssetPro information events) to the
/// ApplicationLogs table. Writes are queued and flushed by <see cref="SqliteLogWriter"/> so logging never blocks requests.
/// </summary>
public sealed class SqliteLoggerProvider(SqliteLogQueue queue, IHttpContextAccessor accessor) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new SqliteLogger(categoryName, queue, accessor);

    public void Dispose() { }

    private sealed class SqliteLogger(string category, SqliteLogQueue queue, IHttpContextAccessor accessor) : ILogger
    {
        // EF Core's own logs would recurse into the writer; hosting noise is not useful in the admin UI.
        private readonly bool _ignored = category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                                         || category == typeof(SqliteLogWriter).FullName;

        private readonly bool _isApp = category.StartsWith("AssetPro", StringComparison.Ordinal);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            !_ignored && logLevel != LogLevel.None && (logLevel >= LogLevel.Warning || (_isApp && logLevel >= LogLevel.Information));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var context = accessor.HttpContext;
            queue.Enqueue(new ApplicationLog
            {
                Timestamp = DateTime.UtcNow,
                Level = logLevel.ToString(),
                Category = Limit(category, 200)!,
                Message = Limit(formatter(state, exception), 4000) ?? "",
                Exception = Limit(exception?.ToString(), 8000),
                TraceId = Activity.Current?.TraceId.ToString() ?? context?.TraceIdentifier,
                RequestPath = Limit(context?.Request.Path.Value, 300)
            });
        }

        private static string? Limit(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
    }
}

public sealed class SqliteLogQueue
{
    private readonly Channel<ApplicationLog> _channel = Channel.CreateBounded<ApplicationLog>(
        new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Enqueue(ApplicationLog entry) => _channel.Writer.TryWrite(entry);

    public ChannelReader<ApplicationLog> Reader => _channel.Reader;
}

public sealed class SqliteLogWriter(SqliteLogQueue queue, IServiceScopeFactory scopes, ILogger<SqliteLogWriter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ApplicationLog>();
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                while (batch.Count < 200 && queue.Reader.TryRead(out var entry)) batch.Add(entry);
                await FlushAsync(batch);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down: write whatever is left.
            while (queue.Reader.TryRead(out var entry)) batch.Add(entry);
            await FlushAsync(batch);
        }
    }

    private async Task FlushAsync(List<ApplicationLog> batch)
    {
        if (batch.Count == 0) return;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ApplicationLogs.AddRange(batch);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Never let log persistence failures crash the app; console logging still has the original entries.
            logger.LogDebug(ex, "Could not persist {Count} application log entries", batch.Count);
        }
        finally
        {
            batch.Clear();
        }
    }
}
