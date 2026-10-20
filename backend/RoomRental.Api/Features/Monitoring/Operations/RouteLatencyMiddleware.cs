using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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

        try
        {
            await _next(context);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            var statusCode = context.Response?.StatusCode ?? 200;
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
