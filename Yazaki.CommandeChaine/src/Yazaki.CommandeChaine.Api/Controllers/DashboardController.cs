using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yazaki.CommandeChaine.Core.Entities.Events;
using Yazaki.CommandeChaine.Infrastructure.Persistence;

namespace Yazaki.CommandeChaine.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(CommandeChaineDbContext db) : ControllerBase
{
    [HttpGet("delay")]
    public async Task<ActionResult<List<DelayPointDto>>> GetDelaySeries(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? chainId,
        [FromQuery] Guid? tableId,
        [FromQuery] string? cause,
        [FromQuery] string? bucket,
        CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = NormalizeRange(from, to);

        var allEvents = await db.QualityEvents.AsNoTracking().ToListAsync(cancellationToken);

        var filtered = allEvents
            .Where(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc)
            .Where(x => x.DelayPercent != null);

        if (chainId.HasValue)
        {
            filtered = filtered.Where(x => x.ChainId == chainId);
        }

        if (tableId.HasValue)
        {
            filtered = filtered.Where(x => x.TableId == tableId);
        }

        if (!string.IsNullOrWhiteSpace(cause))
        {
            var parsed = ParseCause(cause);
            if (parsed is null)
            {
                return Ok(new List<DelayPointDto>());
            }

            filtered = filtered.Where(x => x.Cause == parsed);
        }

        var bucketKey = NormalizeBucket(bucket);

        var rows = filtered.ToList();

        var grouped = rows
            .GroupBy(x => BucketTime(x.OccurredAtUtc, bucketKey))
            .OrderBy(g => g.Key)
            .Select(g => new DelayPointDto(
                g.Key,
                g.Average(x => x.DelayPercent ?? 0.0),
                g.Count()))
            .ToList();

        return grouped;
    }

    [HttpGet("stops")]
    public async Task<ActionResult<List<StopSummaryDto>>> GetStopsSummary(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? chainId,
        [FromQuery] Guid? tableId,
        [FromQuery] string? cause,
        CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = NormalizeRange(from, to);

        // SQLite stores DateTimeOffset as TEXT; filter in memory.
        var allEvents = await db.QualityEvents.AsNoTracking().ToListAsync(cancellationToken);

        var filtered = allEvents
            .Where(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc)
            .Where(x => x.Kind == QualityEventKind.Stop)
            .Where(x => x.Cause != null);

        if (chainId.HasValue)
        {
            filtered = filtered.Where(x => x.ChainId == chainId);
        }

        if (tableId.HasValue)
        {
            filtered = filtered.Where(x => x.TableId == tableId);
        }

        if (!string.IsNullOrWhiteSpace(cause))
        {
            var parsed = ParseCause(cause);
            if (parsed is null)
            {
                return Ok(new List<StopSummaryDto>());
            }

            filtered = filtered.Where(x => x.Cause == parsed);
        }

        var rows = filtered.ToList();

        var grouped = rows
            .GroupBy(x => x.Cause)
            .OrderBy(g => g.Key)
            .Select(g => new StopSummaryDto(
                g.Key?.ToString() ?? "Autre",
                g.Count(),
                g.Sum(x => x.DurationMinutes ?? 0.0)))
            .ToList();

        return grouped;
    }

    [HttpGet("credit")]
    public async Task<ActionResult<List<CreditPointDto>>> GetCreditSeries(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? chainId,
        [FromQuery] Guid? tableId,
        [FromQuery] string? bucket,
        CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = NormalizeRange(from, to);

        var allRows = await db.TimeCreditHistory.AsNoTracking().ToListAsync(cancellationToken);

        var filtered = allRows
            .Where(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc);

        if (chainId.HasValue)
        {
            filtered = filtered.Where(x => x.ChainId == chainId);
        }

        if (tableId.HasValue)
        {
            filtered = filtered.Where(x => x.TableId == tableId);
        }

        var bucketKey = NormalizeBucket(bucket);

        var grouped = filtered
            .GroupBy(x => BucketTime(x.OccurredAtUtc, bucketKey))
            .OrderBy(g => g.Key)
            .Select(g => new CreditPointDto(
                g.Key,
                g.Average(x => x.CreditRatio) * 100.0,
                g.Count()))
            .ToList();

        return grouped;
    }

    private static (DateTimeOffset fromUtc, DateTimeOffset toUtc) NormalizeRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        var now = DateTimeOffset.UtcNow;
        var fromUtc = from?.ToUniversalTime() ?? now.AddDays(-7);
        var toUtc = to?.ToUniversalTime() ?? now;
        if (toUtc < fromUtc)
        {
            (fromUtc, toUtc) = (toUtc, fromUtc);
        }

        return (fromUtc, toUtc);
    }

    private static string NormalizeBucket(string? bucket)
        => string.Equals(bucket, "hour", StringComparison.OrdinalIgnoreCase) ? "hour" : "day";

    private static DateTime BucketTime(DateTimeOffset value, string bucket)
    {
        var utc = value.UtcDateTime;
        if (bucket == "hour")
        {
            return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
        }

        return new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc);
    }

    private static QualityEventCause? ParseCause(string cause)
    {
        return cause.Trim().ToLowerInvariant() switch
        {
            "retard" => QualityEventCause.Retard,
            "panne" => QualityEventCause.Panne,
            "qualite" => QualityEventCause.Qualite,
            "autre" => QualityEventCause.Autre,
            _ => null
        };
    }
}

public sealed record DelayPointDto(DateTime TimestampUtc, double Value, int Count);
public sealed record StopSummaryDto(string Cause, int Count, double TotalDurationMinutes);
public sealed record CreditPointDto(DateTime TimestampUtc, double Value, int Count);
