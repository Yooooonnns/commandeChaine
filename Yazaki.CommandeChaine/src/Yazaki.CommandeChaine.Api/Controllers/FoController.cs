using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Yazaki.CommandeChaine.Api.Hubs;
using Yazaki.CommandeChaine.Api.Services;
using Yazaki.CommandeChaine.Application.Services.SpeedOptimization;
using Yazaki.CommandeChaine.Core.Entities.Fo;
using Yazaki.CommandeChaine.Infrastructure.Persistence;

namespace Yazaki.CommandeChaine.Api.Controllers;

[ApiController]
[Route("api/fo")]
public sealed class FoController(
    CommandeChaineDbContext db,
    HeijunkaLevelingService heijunka,
    MqttCycleTimePublisher mqttPublisher,
    IHubContext<RealtimeHub> hubContext) : ControllerBase
{
    [HttpPost("assign")]
    public async Task<ActionResult<FoAssignResponse>> Assign([FromBody] FoAssignRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FoName))
        {
            return BadRequest("FoName is required");
        }

        if (request.Harnesses.Count == 0)
        {
            return BadRequest("Harness list is empty");
        }

        var chain = await db.Chains
            .Include(x => x.Tables)
            .FirstOrDefaultAsync(x => x.Id == request.ChainId, cancellationToken);

        if (chain is null)
        {
            return NotFound("Chain not found");
        }

        var required = chain.Tables.Count; // Chain capacity = number of boards
        if (request.Harnesses.Count < required)
        {
            return BadRequest($"FO requires at least {required} harnesses for {chain.Tables.Count} boards.");
        }

        var existing = await db.FoBatches
            .Include(x => x.Harnesses)
            .FirstOrDefaultAsync(x => x.ChainId == request.ChainId, cancellationToken);

        if (existing is not null)
        {
            db.FoHarnesses.RemoveRange(existing.Harnesses);
            db.FoBatches.Remove(existing);
        }

        var boardCount = Math.Max(0, chain.Tables.Count);
        var manHours = request.Harnesses
            .OrderBy(x => x.OrderIndex)
            .Take(boardCount)
            .Select(x => x.ProductionTimeMinutes)
            .ToList();

        var recommendation = heijunka.Recommend(
            manHours,
            chain.WorkerCount,
            chain.ProductivityFactor,
            chain.PitchDistanceMeters,
            chain.BalancingTuningK);
        var batch = new FoBatch
        {
            Id = Guid.NewGuid(),
            ChainId = request.ChainId,
            FoName = request.FoName.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RecommendedSpeedRpm = recommendation.RecommendedSpeedRpm,
            CompletionMode = request.CompletionMode,
            Harnesses = request.Harnesses.Select(h => new FoHarness
            {
                Id = Guid.NewGuid(),
                Reference = h.Reference,
                ProductionTimeMinutes = h.ProductionTimeMinutes,
                IsUrgent = h.IsUrgent,
                IsLate = h.IsLate,
                OrderIndex = h.OrderIndex,
                IsCompleted = false
            }).ToList()
        };

        db.FoBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        // Publish initial CT to Raspberry via MQTT
        if (recommendation.CycleTimeMinutes is double ctMinutes && ctMinutes > 0)
        {
            var ctSeconds = Math.Round(ctMinutes * 60.0, 3);
            var jigs = request.Harnesses
                .OrderBy(x => x.OrderIndex)
                .Take(boardCount)
                .Select((h, idx) => new MqttJigPayload(
                    jig_id: h.Reference,
                    status: "ACTIVE",
                    relative_pos: idx / (double)Math.Max(1, boardCount)
                )).ToList();

            await mqttPublisher.PublishCycleTimeAsync(
                lineId: chain.Name,
                calculatedCtSeconds: ctSeconds,
                isRunning: true,
                encoderDelta: 0.0,
                jigs: jigs,
                cancellationToken: cancellationToken);
        }

        return new FoAssignResponse(batch.Id, batch.ChainId, batch.FoName, batch.Harnesses.Count, recommendation.RecommendedSpeedRpm);
    }

    [HttpGet("status/{chainId:guid}")]
    public async Task<ActionResult<FoStatusResponse>> Status(Guid chainId, CancellationToken cancellationToken)
    {
        var batch = await db.FoBatches.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ChainId == chainId, cancellationToken);

        if (batch is null)
        {
            return NotFound();
        }

        var total = await db.FoHarnesses.CountAsync(x => x.FoBatchId == batch.Id, cancellationToken);
        var completed = await db.FoHarnesses.CountAsync(x => x.FoBatchId == batch.Id && x.IsCompleted, cancellationToken);

        return new FoStatusResponse(batch.Id, batch.ChainId, batch.FoName, total, completed, batch.RecommendedSpeedRpm);
    }

    [HttpPost("complete")]
    public async Task<ActionResult<FoStatusResponse>> Complete([FromBody] FoCompleteRequest request, CancellationToken cancellationToken)
    {
        var batch = await db.FoBatches
            .FirstOrDefaultAsync(x => x.ChainId == request.ChainId, cancellationToken);

        if (batch is null)
        {
            return NotFound("FO not found for chain");
        }

        var harness = await db.FoHarnesses
            .FirstOrDefaultAsync(x => x.FoBatchId == batch.Id && x.Reference == request.Reference && !x.IsCompleted, cancellationToken);

        if (harness is null)
        {
            return NotFound("Harness not found");
        }

        harness.IsCompleted = true;
        harness.CompletedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        // Recalculate CT for remaining harnesses and publish to Raspberry
        await RecalculateAndPublishCtAsync(batch.ChainId, batch.Id, cancellationToken);

        var total = await db.FoHarnesses.CountAsync(x => x.FoBatchId == batch.Id, cancellationToken);
        var completed = await db.FoHarnesses.CountAsync(x => x.FoBatchId == batch.Id && x.IsCompleted, cancellationToken);
        return new FoStatusResponse(batch.Id, batch.ChainId, batch.FoName, total, completed, batch.RecommendedSpeedRpm);
    }

    [HttpPost("complete-next")]
    public async Task<ActionResult<FoStatusResponse>> CompleteNext([FromBody] FoCompleteNextRequest request, CancellationToken cancellationToken)
    {
        var batch = await db.FoBatches
            .FirstOrDefaultAsync(x => x.ChainId == request.ChainId, cancellationToken);

        if (batch is null)
        {
            return NotFound("FO not found for chain");
        }

        var remaining = await db.FoHarnesses
            .Where(x => x.FoBatchId == batch.Id && !x.IsCompleted)
            .ToListAsync(cancellationToken);

        var next = remaining
            .OrderBy<FoHarness, int>(x => PriorityRank(x))
            .ThenBy(x => x.OrderIndex)
            .FirstOrDefault();

        if (next is null)
        {
            return BadRequest("All harnesses are already completed");
        }

        next.IsCompleted = true;
        next.CompletedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        // Recalculate CT for remaining harnesses and publish to Raspberry
        await RecalculateAndPublishCtAsync(batch.ChainId, batch.Id, cancellationToken);

        var total = await db.FoHarnesses.CountAsync(x => x.FoBatchId == batch.Id, cancellationToken);
        var completed = await db.FoHarnesses.CountAsync(x => x.FoBatchId == batch.Id && x.IsCompleted, cancellationToken);
        return new FoStatusResponse(batch.Id, batch.ChainId, batch.FoName, total, completed, batch.RecommendedSpeedRpm);
    }

    [HttpGet("current/{chainId:guid}/{tableIndex:int}")]
    public async Task<ActionResult<FoCurrentResponse>> Current(Guid chainId, int tableIndex, CancellationToken cancellationToken)
    {
        var batch = await db.FoBatches.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ChainId == chainId, cancellationToken);

        if (batch is null)
        {
            return NotFound("FO not found for chain");
        }

        var remaining = await db.FoHarnesses.AsNoTracking()
            .Where(x => x.FoBatchId == batch.Id && !x.IsCompleted)
            .ToListAsync(cancellationToken);

        if (remaining.Count == 0)
        {
            return NotFound("No remaining harnesses");
        }

        var ordered = remaining
            .OrderBy<FoHarness, int>(x => PriorityRank(x))
            .ThenBy(x => x.OrderIndex)
            .ToList();

        var idx = Math.Max(0, tableIndex - 1);
        var selected = ordered[idx % ordered.Count];

        return new FoCurrentResponse(selected.Reference, selected.OrderIndex);
    }

    [HttpGet("board-metrics/{chainId:guid}/{tableIndex:int}")]
    public async Task<ActionResult<FoBoardMetricsResponse>> BoardMetrics(Guid chainId, int tableIndex, CancellationToken cancellationToken)
    {
        var batch = await db.FoBatches.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ChainId == chainId, cancellationToken);

        if (batch is null)
        {
            return NotFound("FO not found for chain");
        }

        var allHarnesses = await db.FoHarnesses.AsNoTracking()
            .Where(x => x.FoBatchId == batch.Id)
            .ToListAsync(cancellationToken);

        if (allHarnesses.Count == 0)
        {
            return NotFound("No harnesses in FO");
        }

        var remaining = allHarnesses
            .Where(x => !x.IsCompleted)
            .ToList();

        if (remaining.Count == 0)
        {
            return NotFound("No remaining harnesses");
        }

        var ordered = remaining
            .OrderBy<FoHarness, int>(x => PriorityRank(x))
            .ThenBy(x => x.OrderIndex)
            .ToList();

        var idx = Math.Max(0, tableIndex - 1);
        var selected = ordered[idx % ordered.Count];
        var max = allHarnesses.Max(x => x.ProductionTimeMinutes);

        return new FoBoardMetricsResponse(max, selected.ProductionTimeMinutes, selected.Reference);
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel([FromBody] FoCancelRequest request, CancellationToken cancellationToken)
    {
        var batch = await db.FoBatches
            .Include(x => x.Harnesses)
            .FirstOrDefaultAsync(x => x.ChainId == request.ChainId, cancellationToken);

        if (batch is null)
        {
            return NotFound("FO not found for chain");
        }

        db.FoHarnesses.RemoveRange(batch.Harnesses);
        db.FoBatches.Remove(batch);
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpGet("pending-cables/{chainTableId:guid}")]
    public async Task<ActionResult<List<PendingCableDto>>> GetPendingCables(Guid chainTableId, CancellationToken cancellationToken)
    {
        var table = await db.ChainTables
            .Include(x => x.Chain)
            .FirstOrDefaultAsync(x => x.Id == chainTableId, cancellationToken);

        if (table is null)
        {
            return NotFound("Table not found");
        }

        var batch = await db.FoBatches
            .Include(x => x.Harnesses)
            .FirstOrDefaultAsync(x => x.ChainId == table.ChainId, cancellationToken);

        if (batch is null)
        {
            return Ok(new List<PendingCableDto>());
        }

        // Get all validations for this board
        var validations = await db.BoardCableValidations
            .Where(x => x.ChainTableId == chainTableId)
            .ToListAsync(cancellationToken);

        var validationMap = validations
            .GroupBy(x => x.FoHarnessId)
            .ToDictionary(x => x.Key, x => x.First().Status);

        // Get pending harnesses (not yet completed in the FO)
        var pending = batch.Harnesses
            .Where(x => !x.IsCompleted)
            .Select((h, idx) => new PendingCableDto(
                h.Id,
                h.Reference,
                h.ProductionTimeMinutes,
                h.IsUrgent,
                h.IsLate,
                h.OrderIndex,
                validationMap.TryGetValue(h.Id, out var status) ? status : BoardCableValidationStatus.Pending
            ))
            .OrderBy<PendingCableDto, int>(x => PriorityRankDto(x))
            .ThenBy(x => x.OrderIndex)
            .ToList();

        return Ok(pending);
    }

    [HttpPost("validate-cable")]
    public async Task<ActionResult<CableValidationResultDto>> ValidateCable([FromBody] ValidateCableRequest request, CancellationToken cancellationToken)
    {
        var table = await db.ChainTables.FirstOrDefaultAsync(x => x.Id == request.ChainTableId, cancellationToken);
        if (table is null)
        {
            return NotFound("Table not found");
        }

        var harness = await db.FoHarnesses.FirstOrDefaultAsync(x => x.Id == request.FoHarnessId, cancellationToken);
        if (harness is null)
        {
            return NotFound("Cable not found");
        }

        var existingValidation = await db.BoardCableValidations
            .FirstOrDefaultAsync(x => x.FoHarnessId == request.FoHarnessId && x.ChainTableId == request.ChainTableId, cancellationToken);

        if (existingValidation is null)
        {
            // Create new validation (cable starts on board)
            existingValidation = new BoardCableValidation
            {
                Id = Guid.NewGuid(),
                FoHarnessId = request.FoHarnessId,
                ChainTableId = request.ChainTableId,
                Status = BoardCableValidationStatus.Started,
                StartedAtUtc = DateTimeOffset.UtcNow
            };
            db.BoardCableValidations.Add(existingValidation);
        }
        else if (existingValidation.Status == BoardCableValidationStatus.Started)
        {
            // Mark as completed (cable exits board)
            existingValidation.Status = BoardCableValidationStatus.Completed;
            existingValidation.CompletedAtUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            return BadRequest("Cable validation already completed for this board");
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new CableValidationResultDto(
            harness.Reference,
            existingValidation.Status,
            existingValidation.StartedAtUtc,
            existingValidation.CompletedAtUtc
        ));
    }

    private static int PriorityRankDto(PendingCableDto cable)
    {
        if (cable.IsUrgent && cable.IsLate)
        {
            return 0;
        }

        if (cable.IsUrgent)
        {
            return 1;
        }

        if (cable.IsLate)
        {
            return 3;
        }

        return 2;
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

    private static int PriorityRank(FoHarnessRequest harness)
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

    /// <summary>
    /// Recalculates CT based on remaining (non-completed) harnesses and publishes to Raspberry via MQTT.
    /// Called when a harness exits the chain.
    /// </summary>
    private async Task RecalculateAndPublishCtAsync(Guid chainId, Guid batchId, CancellationToken cancellationToken)
    {
        var chain = await db.Chains
            .AsNoTracking()
            .Include(x => x.Tables)
            .FirstOrDefaultAsync(x => x.Id == chainId, cancellationToken);

        if (chain is null)
        {
            return;
        }

        // Get remaining (non-completed) harnesses
        var remainingHarnesses = await db.FoHarnesses
            .AsNoTracking()
            .Where(x => x.FoBatchId == batchId && !x.IsCompleted)
            .OrderBy(x => x.OrderIndex)
            .ToListAsync(cancellationToken);

        if (remainingHarnesses.Count == 0)
        {
            // All harnesses completed - nothing to calculate
            return;
        }

        // Take harnesses that are currently ON the chain (if using entry/exit tracking)
        // If no harnesses are marked as on-chain, fall back to using remaining harnesses
        // This supports both the new entry/exit flow and legacy complete flow
        var boardCount = Math.Max(0, chain.Tables.Count);
        var onChainHarnesses = remainingHarnesses.Where(x => x.IsOnChain).OrderBy(x => x.ChainPosition).ToList();
        
        List<FoHarness> harnessesForCalculation;
        if (onChainHarnesses.Count > 0)
        {
            // Use on-chain harnesses (new entry/exit flow)
            harnessesForCalculation = onChainHarnesses;
        }
        else
        {
            // Legacy flow: use remaining harnesses up to board count + 2
            var effectiveCount = Math.Min(remainingHarnesses.Count, boardCount);
            harnessesForCalculation = remainingHarnesses.Take(effectiveCount).ToList();
        }

        var manHours = harnessesForCalculation.Select(x => x.ProductionTimeMinutes).ToList();

        // Recalculate speed and CT
        var recommendation = heijunka.Recommend(
            manHours,
            chain.WorkerCount,
            chain.ProductivityFactor,
            chain.PitchDistanceMeters,
            chain.BalancingTuningK);

        // Update batch with new recommended speed
        var batch = await db.FoBatches.FirstOrDefaultAsync(x => x.Id == batchId, cancellationToken);
        if (batch is not null)
        {
            batch.RecommendedSpeedRpm = recommendation.RecommendedSpeedRpm;
            await db.SaveChangesAsync(cancellationToken);

            // Broadcast speed update to Desktop via SignalR
            await hubContext.Clients.Group(RealtimeHub.ChainGroup(chainId))
                .SendAsync("SpeedUpdated", new
                {
                    ChainId = chainId,
                    RecommendedSpeedRpm = recommendation.RecommendedSpeedRpm,
                    CycleTimeMinutes = recommendation.CycleTimeMinutes,
                    HarnessesOnChain = harnessesForCalculation.Count
                }, cancellationToken);
        }

        // Publish updated CT to Raspberry via MQTT
        if (recommendation.CycleTimeMinutes is double ctMinutes && ctMinutes > 0)
        {
            var ctSeconds = Math.Round(ctMinutes * 60.0, 3);
            var jigs = harnessesForCalculation.Select((h, idx) => new MqttJigPayload(
                jig_id: h.Reference,
                status: onChainHarnesses.Contains(h) ? "ON_CHAIN" : "PENDING",
                relative_pos: idx / (double)Math.Max(1, harnessesForCalculation.Count)
            )).ToList();

            await mqttPublisher.PublishCycleTimeAsync(
                lineId: chain.Name,
                calculatedCtSeconds: ctSeconds,
                isRunning: true,
                encoderDelta: 0.0,
                jigs: jigs,
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Harness enters the chain (barcode scanned at entry point).
    /// Recalculates CT based on harnesses currently on the chain.
    /// </summary>
    [HttpPost("harness-entry")]
    public async Task<ActionResult<HarnessEntryExitResponse>> HarnessEntry([FromBody] HarnessEntryRequest request, CancellationToken cancellationToken)
    {
        var batch = await db.FoBatches
            .FirstOrDefaultAsync(x => x.ChainId == request.ChainId, cancellationToken);

        if (batch is null)
        {
            return NotFound("FO not found for chain");
        }

        var harness = await db.FoHarnesses
            .FirstOrDefaultAsync(x => x.FoBatchId == batch.Id && x.Reference == request.Reference && !x.IsOnChain && !x.IsCompleted, cancellationToken);

        if (harness is null)
        {
            return NotFound("Harness not found or already on chain");
        }

        // Get current max position on chain
        var maxPosition = await db.FoHarnesses
            .Where(x => x.FoBatchId == batch.Id && x.IsOnChain)
            .MaxAsync(x => (int?)x.ChainPosition, cancellationToken) ?? -1;

        harness.IsOnChain = true;
        harness.EnteredChainAtUtc = DateTimeOffset.UtcNow;
        harness.ChainPosition = maxPosition + 1;
        await db.SaveChangesAsync(cancellationToken);

        // Recalculate CT for harnesses on chain and publish to Raspberry
        await RecalculateAndPublishCtAsync(batch.ChainId, batch.Id, cancellationToken);

        var onChainCount = await db.FoHarnesses.CountAsync(x => x.FoBatchId == batch.Id && x.IsOnChain, cancellationToken);
        return new HarnessEntryExitResponse(harness.Reference, "entered", onChainCount, batch.RecommendedSpeedRpm);
    }

    /// <summary>
    /// Harness exits the chain (barcode scanned at exit point).
    /// Marks harness as completed and recalculates CT.
    /// </summary>
    [HttpPost("harness-exit")]
    public async Task<ActionResult<HarnessEntryExitResponse>> HarnessExit([FromBody] HarnessExitRequest request, CancellationToken cancellationToken)
    {
        var batch = await db.FoBatches
            .FirstOrDefaultAsync(x => x.ChainId == request.ChainId, cancellationToken);

        if (batch is null)
        {
            return NotFound("FO not found for chain");
        }

        var harness = await db.FoHarnesses
            .FirstOrDefaultAsync(x => x.FoBatchId == batch.Id && x.Reference == request.Reference && x.IsOnChain && !x.IsCompleted, cancellationToken);

        if (harness is null)
        {
            return NotFound("Harness not found or not on chain");
        }

        // Mark as exited and completed
        harness.IsOnChain = false;
        harness.IsCompleted = true;
        harness.CompletedAtUtc = DateTimeOffset.UtcNow;

        // Shift positions of remaining harnesses
        var harnessesAfter = await db.FoHarnesses
            .Where(x => x.FoBatchId == batch.Id && x.IsOnChain && x.ChainPosition > harness.ChainPosition)
            .ToListAsync(cancellationToken);

        foreach (var h in harnessesAfter)
        {
            h.ChainPosition--;
        }

        await db.SaveChangesAsync(cancellationToken);

        // Recalculate CT for remaining harnesses on chain and publish to Raspberry
        await RecalculateAndPublishCtAsync(batch.ChainId, batch.Id, cancellationToken);

        var onChainCount = await db.FoHarnesses.CountAsync(x => x.FoBatchId == batch.Id && x.IsOnChain, cancellationToken);
        return new HarnessEntryExitResponse(harness.Reference, "exited", onChainCount, batch.RecommendedSpeedRpm);
    }

    /// <summary>
    /// Get harnesses currently on the chain.
    /// </summary>
    [HttpGet("on-chain/{chainId:guid}")]
    public async Task<ActionResult<List<OnChainHarnessDto>>> GetOnChainHarnesses(Guid chainId, CancellationToken cancellationToken)
    {
        var batch = await db.FoBatches
            .FirstOrDefaultAsync(x => x.ChainId == chainId, cancellationToken);

        if (batch is null)
        {
            return Ok(new List<OnChainHarnessDto>());
        }

        var harnesses = await db.FoHarnesses
            .Where(x => x.FoBatchId == batch.Id && x.IsOnChain)
            .OrderBy(x => x.ChainPosition)
            .Select(x => new OnChainHarnessDto(x.Reference, x.ProductionTimeMinutes, x.ChainPosition, x.EnteredChainAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(harnesses);
    }
}

public sealed record FoAssignRequest(Guid ChainId, string FoName, List<FoHarnessRequest> Harnesses, CompletionMode CompletionMode = CompletionMode.Manual);
public sealed record FoHarnessRequest(string Reference, int ProductionTimeMinutes, bool IsUrgent, bool IsLate, int OrderIndex);
public sealed record FoAssignResponse(Guid FoId, Guid ChainId, string FoName, int HarnessCount, double RecommendedSpeedRpm);
public sealed record FoStatusResponse(Guid FoId, Guid ChainId, string FoName, int HarnessCount, int CompletedCount, double RecommendedSpeedRpm);
public sealed record FoCompleteRequest(Guid ChainId, string Reference);
public sealed record FoCompleteNextRequest(Guid ChainId);
public sealed record FoCancelRequest(Guid ChainId);
public sealed record FoCurrentResponse(string Reference, int OrderIndex);
public sealed record FoBoardMetricsResponse(int MaxProductionTimeMinutes, int CurrentProductionTimeMinutes, string Reference);
public sealed record PendingCableDto(Guid Id, string Reference, int ProductionTimeMinutes, bool IsUrgent, bool IsLate, int OrderIndex, BoardCableValidationStatus ValidationStatus);
public sealed record ValidateCableRequest(Guid FoHarnessId, Guid ChainTableId);
public sealed record CableValidationResultDto(string CableReference, BoardCableValidationStatus Status, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc);

// Entry/Exit records
public sealed record HarnessEntryRequest(Guid ChainId, string Reference);
public sealed record HarnessExitRequest(Guid ChainId, string Reference);
public sealed record HarnessEntryExitResponse(string Reference, string Action, int OnChainCount, double RecommendedSpeedRpm);
public sealed record OnChainHarnessDto(string Reference, int ProductionTimeMinutes, int ChainPosition, DateTimeOffset? EnteredChainAtUtc);
