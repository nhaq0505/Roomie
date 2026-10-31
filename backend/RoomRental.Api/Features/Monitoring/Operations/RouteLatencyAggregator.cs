using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Features.Monitoring.Operations;

public sealed record RouteLatencySnapshot(
    string RouteTemplate,
    string HttpMethod,
    int RequestCount,
    int Status5xxCount,
    double? P95LatencyMs,
    double? AvgLatencyMs,
    double? MaxLatencyMs
);

public sealed record OperationsOverviewSnapshot(
    string OverallStatus,
    double? Api5xxRatePercent,
    double? LatencyP95Ms,
    long? TotalRequestsLastHour,
    bool? DatabaseConnected,
    string? DatabaseStatus,
    string? Window = "1h"
);

public sealed record WindowLatencySummary(
    int TotalRequests,
    int Status5xxCount,
    double? Api5xxRatePercent,
    double? LatencyP95Ms,
    IReadOnlyList<LatencyHistogramBucketDto> HistogramBuckets
);

public interface IRouteLatencyAggregator
{
    void Record(string routeTemplate, string httpMethod, int statusCode, double latencyMs);
    IReadOnlyList<RouteLatencySnapshot> GetRouteSnapshots(TimeSpan window);
    OperationsOverviewSnapshot GetOverviewSnapshot(TimeSpan window, bool? dbConnected = null, string? dbStatus = null);
    WindowLatencySummary GetWindowSummary(TimeSpan window);
    IReadOnlyList<OperationalRouteMinuteBucket> DrainCompletedMinuteBuckets(long beforeMinuteEpoch);
    void UnmarkFlushedMinutes(IEnumerable<long> minuteEpochs);
    void Reset();
}

public sealed partial class RouteLatencyAggregator : IRouteLatencyAggregator
{
    public const string UnmatchedApiRoute = "/api/(unmatched)";
    public const string UnmatchedRoute = "/(unmatched)";
    public const string StaticRoute = "/(static)";
    public const string OverflowRoute = "/(overflow)";
    // Hard cap on distinct (route, method, status) keys per minute so no caller can grow RAM or
    // RouteMinuteBuckets rows without bound; real route templates stay far below this.
    public const int MaxKeysPerMinute = 512;
    private static readonly HashSet<string> KnownMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"
    };

    private readonly TimeProvider _clock;
    // ponytail: one process, up to 24h since restart; use persisted buckets for cross-restart/multi-replica views.
    private readonly ConcurrentDictionary<long, MinuteBucketCollection> _minuteBuckets = new();
    private readonly ConcurrentDictionary<long, byte> _flushedMinutes = new();

    public RouteLatencyAggregator(TimeProvider clock)
    {
        _clock = clock;
    }

    public void Record(string routeTemplate, string httpMethod, int statusCode, double latencyMs)
    {
        var now = _clock.GetUtcNow();
        var minuteEpoch = now.ToUnixTimeSeconds() / 60;
        var normalizedRoute = NormalizeRoute(routeTemplate);
        var normalizedMethod = NormalizeMethod(httpMethod);

        var collection = _minuteBuckets.GetOrAdd(minuteEpoch, _ => new MinuteBucketCollection(minuteEpoch));
        collection.Record(normalizedRoute, normalizedMethod, statusCode, latencyMs);

        // Keep the longest supported window, including minutes already flushed to the DB.
        PruneOldBuckets(minuteEpoch - 1440);
    }

    public IReadOnlyList<RouteLatencySnapshot> GetRouteSnapshots(TimeSpan window)
    {
        var now = _clock.GetUtcNow();
        var currentMinute = now.ToUnixTimeSeconds() / 60;
        var windowMinutes = (long)Math.Ceiling(window.TotalMinutes);
        var minMinute = currentMinute - windowMinutes;

        var aggregatedByRoute = new Dictionary<(string Route, string Method), RouteHistogramAccumulator>();

        foreach (var (minute, collection) in _minuteBuckets)
        {
            if (minute > minMinute && minute <= currentMinute)
            {
                collection.AggregateInto(aggregatedByRoute);
            }
        }

        var results = new List<RouteLatencySnapshot>();
        foreach (var ((route, method), acc) in aggregatedByRoute)
        {
            if (acc.RequestCount > 0)
            {
                results.Add(new RouteLatencySnapshot(
                    RouteTemplate: route,
                    HttpMethod: method,
                    RequestCount: acc.RequestCount,
                    Status5xxCount: acc.Status5xxCount,
                    P95LatencyMs: acc.CalculateP95(),
                    AvgLatencyMs: acc.RequestCount > 0 ? Math.Round(acc.TotalLatencyMs / acc.RequestCount, 2) : null,
                    MaxLatencyMs: acc.MaxLatencyMs > 0 ? Math.Round(acc.MaxLatencyMs, 2) : null
                ));
            }
        }

        return results.OrderByDescending(r => r.RequestCount).ToList();
    }

    public WindowLatencySummary GetWindowSummary(TimeSpan window)
    {
        var now = _clock.GetUtcNow();
        var currentMinute = now.ToUnixTimeSeconds() / 60;
        var windowMinutes = (long)Math.Ceiling(window.TotalMinutes);
        var minMinute = currentMinute - windowMinutes;

        var globalAcc = new RouteHistogramAccumulator();

        foreach (var (minute, collection) in _minuteBuckets)
        {
            if (minute > minMinute && minute <= currentMinute)
            {
                collection.AggregateGlobal(globalAcc);
            }
        }

        var totalRequests = globalAcc.RequestCount;
        var total5xx = globalAcc.Status5xxCount;

        double? rate5xx = null;
        if (totalRequests > 0)
        {
            rate5xx = Math.Round((double)total5xx / totalRequests * 100.0, 2);
        }

        double? p95 = totalRequests > 0 ? globalAcc.CalculateP95() : null;

        return new WindowLatencySummary(
            TotalRequests: totalRequests,
            Status5xxCount: total5xx,
            Api5xxRatePercent: rate5xx,
            LatencyP95Ms: p95,
            HistogramBuckets: totalRequests == 0 ? [] : [
                new("≤50 ms", globalAcc.Le50),
                new("50–100 ms", globalAcc.Le100),
                new("100–250 ms", globalAcc.Le250),
                new("250–500 ms", globalAcc.Le500),
                new("500–1000 ms", globalAcc.Le1000),
                new("1000–2000 ms", globalAcc.Le2000),
                new(">2000 ms", globalAcc.Gt2000, true)
            ]
        );
    }

    public OperationsOverviewSnapshot GetOverviewSnapshot(TimeSpan window, bool? dbConnected = null, string? dbStatus = null)
    {
        var summary = GetWindowSummary(window);
        var windowMinutes = (long)Math.Ceiling(window.TotalMinutes);

        var overallStatus = "Healthy";
        if (dbConnected == false || (summary.Api5xxRatePercent != null && summary.Api5xxRatePercent >= 10.0 && summary.TotalRequests >= 100) ||
            (summary.LatencyP95Ms != null && summary.LatencyP95Ms >= 2000.0 && summary.TotalRequests >= 100))
        {
            overallStatus = "Unhealthy";
        }
        else if (dbStatus == "Degraded" || (summary.Api5xxRatePercent != null && summary.Api5xxRatePercent >= 5.0 && summary.TotalRequests >= 100) ||
            (summary.LatencyP95Ms != null && summary.LatencyP95Ms >= 1000.0 && summary.TotalRequests >= 100))
        {
            overallStatus = "Degraded";
        }

        return new OperationsOverviewSnapshot(
            OverallStatus: overallStatus,
            Api5xxRatePercent: summary.Api5xxRatePercent,
            LatencyP95Ms: summary.LatencyP95Ms,
            TotalRequestsLastHour: summary.TotalRequests,
            DatabaseConnected: dbConnected,
            DatabaseStatus: dbStatus ?? (dbConnected == true ? "Healthy" : (dbConnected == false ? "Unhealthy" : null)),
            Window: $"{windowMinutes / 60}h" == "1h" ? "1h" : $"{windowMinutes}m"
        );
    }

    public IReadOnlyList<OperationalRouteMinuteBucket> DrainCompletedMinuteBuckets(long beforeMinuteEpoch)
    {
        var drained = new List<OperationalRouteMinuteBucket>();
        foreach (var minute in _minuteBuckets.Keys)
        {
            if (minute < beforeMinuteEpoch)
            {
                if (_minuteBuckets.TryGetValue(minute, out var collection) && _flushedMinutes.TryAdd(minute, 0))
                {
                    drained.AddRange(collection.ToEntityBuckets());
                }
            }
        }
        return drained;
    }

    public void UnmarkFlushedMinutes(IEnumerable<long> minuteEpochs)
    {
        foreach (var minute in minuteEpochs)
        {
            _flushedMinutes.TryRemove(minute, out _);
        }
    }

    public void Reset()
    {
        _minuteBuckets.Clear();
        _flushedMinutes.Clear();
    }

    private void PruneOldBuckets(long cutoffMinute)
    {
        foreach (var minute in _minuteBuckets.Keys)
        {
            if (minute < cutoffMinute)
            {
                _minuteBuckets.TryRemove(minute, out _);
                _flushedMinutes.TryRemove(minute, out _);
            }
        }
    }

    public static string NormalizeMethod(string? httpMethod) =>
        !string.IsNullOrWhiteSpace(httpMethod) && KnownMethods.Contains(httpMethod)
            ? httpMethod.ToUpperInvariant()
            : "OTHER";

    public static string NormalizeRoute(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return "/";
        
        var normalized = rawPath.Split('?')[0].Trim();
        if (!normalized.StartsWith('/')) normalized = "/" + normalized;

        // Replace GUIDs: /users/a3b683b5-245c-4d37-8ec9-232a93902f23 -> /users/{id}
        normalized = GuidRegex().Replace(normalized, "{id}");
        // Replace numeric IDs: /invoices/12345 -> /invoices/{id}
        normalized = NumericIdRegex().Replace(normalized, "/{id}");

        return normalized;
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled)]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"/(?:\d+)(?=/|$)", RegexOptions.Compiled)]
    private static partial Regex NumericIdRegex();

    private sealed class MinuteBucketCollection
    {
        public long MinuteEpoch { get; }
        private readonly ConcurrentDictionary<(string Route, string Method, int StatusCode), RouteHistogramAccumulator> _byRouteStatus = new();

        public MinuteBucketCollection(long minuteEpoch)
        {
            MinuteEpoch = minuteEpoch;
        }

        public void Record(string route, string method, int statusCode, double latencyMs)
        {
            var key = (route, method, statusCode);
            if (!_byRouteStatus.ContainsKey(key) && _byRouteStatus.Count >= MaxKeysPerMinute)
                key = (OverflowRoute, method, statusCode);
            var acc = _byRouteStatus.GetOrAdd(key, _ => new RouteHistogramAccumulator());
            acc.Add(statusCode, latencyMs);
        }

        public void AggregateInto(Dictionary<(string Route, string Method), RouteHistogramAccumulator> target)
        {
            foreach (var ((route, method, _), sourceAcc) in _byRouteStatus)
            {
                var key = (route, method);
                if (!target.TryGetValue(key, out var acc))
                {
                    acc = new RouteHistogramAccumulator();
                    target[key] = acc;
                }
                acc.MergeFrom(sourceAcc);
            }
        }

        public void AggregateGlobal(RouteHistogramAccumulator target)
        {
            foreach (var sourceAcc in _byRouteStatus.Values)
            {
                target.MergeFrom(sourceAcc);
            }
        }

        public IReadOnlyList<OperationalRouteMinuteBucket> ToEntityBuckets()
        {
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(MinuteEpoch * 60);
            var list = new List<OperationalRouteMinuteBucket>();

            foreach (var ((route, method, statusCode), acc) in _byRouteStatus)
            {
                if (acc.RequestCount > 0)
                {
                    list.Add(new OperationalRouteMinuteBucket
                    {
                        MinuteTimestamp = timestamp,
                        RouteTemplate = route,
                        HttpMethod = method,
                        StatusCode = statusCode,
                        RequestCount = acc.RequestCount,
                        LatencyLe50ms = acc.Le50,
                        LatencyLe100ms = acc.Le100,
                        LatencyLe250ms = acc.Le250,
                        LatencyLe500ms = acc.Le500,
                        LatencyLe1000ms = acc.Le1000,
                        LatencyLe2000ms = acc.Le2000,
                        LatencyGt2000ms = acc.Gt2000,
                        TotalLatencyMs = acc.TotalLatencyMs,
                        MaxLatencyMs = acc.MaxLatencyMs
                    });
                }
            }

            return list;
        }
    }

    internal sealed class RouteHistogramAccumulator
    {
        private int _requestCount;
        private int _status5xxCount;
        private int _le50;
        private int _le100;
        private int _le250;
        private int _le500;
        private int _le1000;
        private int _le2000;
        private int _gt2000;
        private double _totalLatencyMs;
        private double _maxLatencyMs;
        private readonly object _lock = new();

        public int RequestCount => _requestCount;
        public int Status5xxCount => _status5xxCount;
        public int Le50 => _le50;
        public int Le100 => _le100;
        public int Le250 => _le250;
        public int Le500 => _le500;
        public int Le1000 => _le1000;
        public int Le2000 => _le2000;
        public int Gt2000 => _gt2000;
        public double TotalLatencyMs => _totalLatencyMs;
        public double MaxLatencyMs => _maxLatencyMs;

        public void Add(int statusCode, double latencyMs)
        {
            lock (_lock)
            {
                _requestCount++;
                if (statusCode >= 500 && statusCode < 600)
                {
                    _status5xxCount++;
                }

                _totalLatencyMs += latencyMs;
                if (latencyMs > _maxLatencyMs)
                {
                    _maxLatencyMs = latencyMs;
                }

                if (latencyMs <= 50.0) _le50++;
                else if (latencyMs <= 100.0) _le100++;
                else if (latencyMs <= 250.0) _le250++;
                else if (latencyMs <= 500.0) _le500++;
                else if (latencyMs <= 1000.0) _le1000++;
                else if (latencyMs <= 2000.0) _le2000++;
                else _gt2000++;
            }
        }

        public void MergeFrom(RouteHistogramAccumulator other)
        {
            lock (_lock)
            {
                _requestCount += other._requestCount;
                _status5xxCount += other._status5xxCount;
                _le50 += other._le50;
                _le100 += other._le100;
                _le250 += other._le250;
                _le500 += other._le500;
                _le1000 += other._le1000;
                _le2000 += other._le2000;
                _gt2000 += other._gt2000;
                _totalLatencyMs += other._totalLatencyMs;
                if (other._maxLatencyMs > _maxLatencyMs)
                {
                    _maxLatencyMs = other._maxLatencyMs;
                }
            }
        }

        public double? CalculateP95()
        {
            lock (_lock)
            {
                if (_requestCount == 0) return null;
                var targetRank = 0.95 * _requestCount;
                var cum = 0;

                // Bucket 0: <= 50
                cum += _le50;
                if (cum >= targetRank)
                {
                    return Math.Round((targetRank / _le50) * 50.0, 2);
                }

                // Bucket 1: 50 - 100
                var prev = cum;
                cum += _le100;
                if (cum >= targetRank)
                {
                    return Math.Round(50.0 + ((targetRank - prev) / _le100) * 50.0, 2);
                }

                // Bucket 2: 100 - 250
                prev = cum;
                cum += _le250;
                if (cum >= targetRank)
                {
                    return Math.Round(100.0 + ((targetRank - prev) / _le250) * 150.0, 2);
                }

                // Bucket 3: 250 - 500
                prev = cum;
                cum += _le500;
                if (cum >= targetRank)
                {
                    return Math.Round(250.0 + ((targetRank - prev) / _le500) * 250.0, 2);
                }

                // Bucket 4: 500 - 1000
                prev = cum;
                cum += _le1000;
                if (cum >= targetRank)
                {
                    return Math.Round(500.0 + ((targetRank - prev) / _le1000) * 500.0, 2);
                }

                // Bucket 5: 1000 - 2000
                prev = cum;
                cum += _le2000;
                if (cum >= targetRank)
                {
                    return Math.Round(1000.0 + ((targetRank - prev) / _le2000) * 1000.0, 2);
                }

                // Bucket 6: > 2000
                return Math.Round(Math.Max(2000.0, _maxLatencyMs), 2);
            }
        }
    }
}
