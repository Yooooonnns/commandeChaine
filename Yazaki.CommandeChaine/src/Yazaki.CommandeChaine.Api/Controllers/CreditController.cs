using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yazaki.CommandeChaine.Core.Entities.Events;
using Yazaki.CommandeChaine.Core.Entities.Fo;
using Yazaki.CommandeChaine.Infrastructure.Persistence;

namespace Yazaki.CommandeChaine.Api.Controllers;

[ApiController]
[Route("api/credit")]
public sealed class CreditController(CommandeChaineDbContext db) : ControllerBase
{
    [HttpPost("update")]
    public async Task<ActionResult<List<TableCreditDto>>> Update([FromBody] CreditUpdateRequest request, CancellationToken cancellationToken)
    {
        if (request.ChainId == Guid.Empty)
        {
            return BadRequest("ChainId is required");
        }

        if (request.Tables.Count == 0)
        {
            return Ok(new List<TableCreditDto>());
        }

        var tables = await db.ChainTables
            .Where(x => x.ChainId == request.ChainId)
            .ToListAsync(cancellationToken);

        if (tables.Count == 0)
        {
            return Ok(new List<TableCreditDto>());
        }

        var tableById = tables.ToDictionary(x => x.Id, x => x);
        var now = request.OccurredAtUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;

        var batch = await db.FoBatches.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ChainId == request.ChainId, cancellationToken);

        var allHarnesses = batch is null
            ? new List<FoHarness>()
            : await db.FoHarnesses.AsNoTracking()
                .Where(x => x.FoBatchId == batch.Id)
                .ToListAsync(cancellationToken);

        var remaining = allHarnesses.Where(x => !x.IsCompleted).ToList();
        var ordered = remaining
            .OrderBy(PriorityRank)
            .ThenBy(x => x.OrderIndex)
            .ToList();

        var maxMinutes = allHarnesses.Count == 0 ? 0 : allHarnesses.Max(x => x.ProductionTimeMinutes);

        var result = new List<TableCreditDto>();
        var historyRows = new List<TimeCreditHistory>();

        foreach (var update in request.Tables)
        {
            if (!tableById.TryGetValue(update.TableId, out var table))
            {
                continue;
            }

            var progress = Math.Clamp(update.ProgressRatio, 0, 1);
            var targetRatio = 0.0;
            if (ordered.Count > 0 && maxMinutes > 0)
            {
                var idx = Math.Max(0, table.Index - 1);
                var selected = ordered[idx % ordered.Count];
                targetRatio = Math.Clamp((double)selected.ProductionTimeMinutes / maxMinutes, 0, 1);
            }

            var creditRatio = targetRatio - progress;
            var creditMinutes = maxMinutes > 0 ? creditRatio * maxMinutes : 0;

            table.TimeCreditRatio = creditRatio;
            table.TimeCreditMinutes = creditMinutes;
            table.TimeCreditTargetRatio = targetRatio;
            table.TimeCreditUpdatedAtUtc = now;

            historyRows.Add(new TimeCreditHistory
            {
                Id = Guid.NewGuid(),
                ChainId = request.ChainId,
                TableId = table.Id,
                ProgressRatio = progress,
                TargetRatio = targetRatio,
                CreditRatio = creditRatio,
                CreditMinutes = creditMinutes,
                OccurredAtUtc = now,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

            result.Add(new TableCreditDto(
                table.Id,
                table.Index,
                progress,
                targetRatio,
                creditRatio,
                creditMinutes,
                now));
        }

        if (historyRows.Count > 0)
        {
            db.TimeCreditHistory.AddRange(historyRows);
        }

        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    [HttpGet("chain/{chainId:guid}")]
    public async Task<ActionResult<List<TableCreditDto>>> GetForChain(Guid chainId, CancellationToken cancellationToken)
    {
        var tables = await db.ChainTables.AsNoTracking()
            .Where(x => x.ChainId == chainId)
            .OrderBy(x => x.Index)
            .ToListAsync(cancellationToken);

        var result = tables.Select(table => new TableCreditDto(
            table.Id,
            table.Index,
            null,
            table.TimeCreditTargetRatio,
            table.TimeCreditRatio,
            table.TimeCreditMinutes,
            table.TimeCreditUpdatedAtUtc)).ToList();

        return result;
    }

    private static int PriorityRank(FoHarness harness)
    {
        if (harness.IsUrgent && harness.IsLate)
        {
            return 0;
        }

        if (harness.IsUrgent)
        {
            return 1;
        }

        if (harness.IsLate)
        {
            return 3;
        }

        return 2;
    }
}

public sealed record CreditUpdateRequest(Guid ChainId, List<TableCreditUpdateDto> Tables, DateTimeOffset? OccurredAtUtc);
public sealed record TableCreditUpdateDto(Guid TableId, double ProgressRatio);
public sealed record TableCreditDto(Guid TableId, int TableIndex, double? ProgressRatio, double TargetRatio, double CreditRatio, double CreditMinutes, DateTimeOffset? UpdatedAtUtc);
