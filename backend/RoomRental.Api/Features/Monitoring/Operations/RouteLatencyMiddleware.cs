using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RoomRental.Api.Services;

namespace RoomRental.Api.Features.Monitoring.Operations;

public sealed class RouteLatencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRouteLatencyAggregator _aggregator;

    public RouteLatencyMiddleware(RequestDelegate next, IRouteLatencyAggregator aggregator)
    {
        _next = next;
        _aggregator = aggregator;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        int? capturedStatusCode = null;

        try
        {
            await _next(context);
        }
        catch (ApiException apiEx)
        {
            capturedStatusCode = apiEx.StatusCode;
            throw;
        }
        catch (BadHttpRequestException badReq)
        {
            capturedStatusCode = badReq.StatusCode;
            throw;
        }
        catch (Exception)
        {
            capturedStatusCode = StatusCodes.Status500InternalServerError;
            throw;
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            // If an exception was thrown before an outer exception handler set the response code,
            // capturedStatusCode ensures we record the true intended error status code (e.g. 500 or 4xx).
            // Otherwise, context.Response.StatusCode captures handler-set codes (e.g. 401, 403, 404, 200).
            var statusCode = capturedStatusCode
                ?? (context.Response != null && context.Response.StatusCode >= 400 ? (int?)context.Response.StatusCode : null)
                ?? context.Response?.StatusCode
                ?? 200;

            var method = context.Request.Method;
            var routeTemplate = GetRouteTemplate(context);
            if (!string.IsNullOrWhiteSpace(routeTemplate))
            {
                _aggregator.Record(routeTemplate, method, statusCode, elapsedMs);
            }
        }
    }

    private static string GetRouteTemplate(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is RouteEndpoint routeEndpoint && !string.IsNullOrWhiteSpace(routeEndpoint.RoutePattern.RawText))
        {
            var pattern = routeEndpoint.RoutePattern.RawText;
            if (!pattern.StartsWith('/')) pattern = "/" + pattern;
            return pattern;
        }

        var path = context.Request.Path.Value ?? "/";
        return RouteLatencyAggregator.NormalizeRoute(path);
    }
}
