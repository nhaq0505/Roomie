using System.Security.Claims;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RoomRental.Api.Services;

public sealed class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public static class ApiSupport
{
    public static DateOnly VietnamDate(DateTimeOffset instant)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }

    public static Guid UserId(HttpContext context)
    {
        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : throw new ApiException(StatusCodes.Status401Unauthorized, "Please sign in.");
    }

    public static Guid OwnerId(HttpContext context)
    {
        if (!context.User.IsInRole("Owner"))
            throw new ApiException(StatusCodes.Status403Forbidden, "Owner access is required.");
        return UserId(context);
    }

    public static void RequireAdmin(HttpContext context)
    {
        if (!context.User.IsInRole("Admin"))
            throw new ApiException(StatusCodes.Status403Forbidden, "Administrator access is required.");
    }

    public static string NormalizePeriod(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[4] != '-' ||
            !int.TryParse(value.AsSpan(0, 4), out var year) || !int.TryParse(value.AsSpan(5, 2), out var month) ||
            year is < 2000 or > 2200 || month is < 1 or > 12)
            throw new ApiException(StatusCodes.Status400BadRequest, "Period must use YYYY-MM format.");
        return $"{year:D4}-{month:D2}";
    }

    public static DateOnly PeriodDueDate(string period)
    {
        var year = int.Parse(period.AsSpan(0, 4));
        var month = int.Parse(period.AsSpan(5, 2));
        return new DateOnly(year, month, 1).AddMonths(1).AddDays(4);
    }

    public static string SafePath(PathString path) => path.StartsWithSegments("/api/tenant") || path.StartsWithSegments("/t") || path.StartsWithSegments("/tenant")
        ? "/tenant/[redacted]" : path.Value ?? "";

    public static decimal RoundVnd(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);

    public static string UtcIsoString(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    public static bool IsSameOrigin(HttpRequest request)
    {
        var candidate = request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(candidate)) candidate = request.Headers.Referer.FirstOrDefault();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var origin)) return false;
        var requestHost = request.Host;
        var requestPort = requestHost.Port ?? (request.IsHttps ? 443 : 80);
        var originPort = origin.IsDefaultPort ? (origin.Scheme == "https" ? 443 : 80) : origin.Port;
        return string.Equals(origin.Scheme, request.IsHttps ? "https" : "http", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(origin.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase) && requestPort == originPort;
    }
}

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException exception)
        {
            if (context.Response.HasStarted) throw;
            context.Response.Clear();
            context.Response.StatusCode = exception.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { message = exception.Message });
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client has gone away; there is no response left to write.
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("A concurrent update was rejected for {Path}.", ApiSupport.SafePath(context.Request.Path));
            await WriteConflictAsync(context, "The record changed while you were editing it. Refresh and retry.");
        }
        catch (Exception exception) when (IsDatabaseConflict(exception))
        {
            logger.LogInformation("A database concurrency conflict was rejected for {Path}.", ApiSupport.SafePath(context.Request.Path));
            await WriteConflictAsync(context, "Another request changed this record. Refresh and retry.");
        }
        catch (Exception exception)
        {
            logger.LogError("Unhandled request error {ErrorType} for {Path}", exception.GetType().Name, ApiSupport.SafePath(context.Request.Path));
            if (context.Response.HasStarted) throw;
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { message = "An unexpected server error occurred." });
        }
    }

    private static async Task WriteConflictAsync(HttpContext context, string message)
    {
        if (context.Response.HasStarted) throw new InvalidOperationException("The response has already started.");
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { message });
    }

    private static bool IsDatabaseConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && postgres.SqlState is "40001" or "40P01" or "23505" or "55P03")
                return true;
            if (current is SqliteException sqlite && sqlite.SqliteErrorCode is 5 or 6 or 19)
                return true;
        }
        return false;
    }
}
