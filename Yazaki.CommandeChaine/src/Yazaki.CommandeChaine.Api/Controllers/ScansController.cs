using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Yazaki.CommandeChaine.Api.Hubs;
using Yazaki.CommandeChaine.Api.Services;
using Yazaki.CommandeChaine.Application.Services.SpeedOptimization;
using Yazaki.CommandeChaine.Core.Entities.Events;
using Yazaki.CommandeChaine.Core.Entities.Speeds;
using Yazaki.CommandeChaine.Infrastructure.Persistence;

namespace Yazaki.CommandeChaine.Api.Controllers;

[ApiController]
[Route("api/scans")]
public sealed class ScansController(
    CommandeChaineDbContext db,
    IHubContext<RealtimeHub> hub,
    HeijunkaLevelingService heijunka,
    RaspberryPiClient raspberryPi,
    MqttCycleTimePublisher mqttPublisher) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ScanIngestResponse>> Ingest([FromBody] ScanIngestRequest request, CancellationToken cancellationToken)
    {
        var barcode = request.Barcode?.Trim();
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BadRequest("Barcode is required");
        }

        var now = DateTimeOffset.UtcNow;

        var scan = new BarcodeScanEvent
        {
            Id = Guid.NewGuid(),
            Barcode = barcode,
            ScannerId = string.IsNullOrWhiteSpace(request.ScannerId) ? null : request.ScannerId.Trim(),
            FoName = string.IsNullOrWhiteSpace(request.Fo) ? null : request.Fo.Trim(),
            HarnessType = string.IsNullOrWhiteSpace(request.HarnessType) ? null : request.HarnessType.Trim(),
            ProductionTimeMinutes = request.ProductionTimeInMinutes,
            IsUrgent = request.IsUrgent,
            ChainId = request.ChainId,
            TableId = request.TableId,
            ScannedAtUtc = request.ScannedAtUtc ?? now,
            CreatedAtUtc = now
        };

        db.BarcodeScanEvents.Add(scan);
        await db.SaveChangesAsync(cancellationToken);

        // Broadcast scan to chain viewers (Parc view removed).
        if (scan.ChainId is Guid scanChainId)
        {
            await hub.Clients.Group(RealtimeHub.ChainGroup(scanChainId)).SendAsync("ScanReceived", new
            {
                scan.Id,
                scan.Barcode,
                scan.ScannerId,
                scan.ChainId,
                scan.TableId,
                scan.ScannedAtUtc
            }, cancellationToken);
        }

        SpeedRecommendationResult? recommendation = null;
        if (scan.ChainId is Guid chainId)
        {
            recommendation = await ComputeHeijunkaRecommendationAsync(chainId, scan.FoName, cancellationToken);
            if (recommendation is not null)
            {
                await hub.Clients.Group(RealtimeHub.ChainGroup(chainId)).SendAsync("SpeedRecommended", new
                {
                    ChainId = chainId,
                    recommendation.RecommendedSpeedRpm,
                    recommendation.Confidence,
                    recommendation.Rationale,
                    ComputedAtUtc = now
                }, cancellationToken);

                var chain = await db.Chains.FirstOrDefaultAsync(x => x.Id == chainId, cancellationToken);
                if (chain is not null)
                {
                    var publishedCycleTime = false;

                    if (recommendation.CycleTimeMinutes is double ctMinutes && ctMinutes > 0)
                    {
                        var ctSeconds = Math.Round(ctMinutes * 60.0, 3);
                        var jigs = new List<MqttJigPayload>
                        {
                            new(
                                jig_id: scan.TableId?.ToString() ?? "UNASSIGNED",
                                status: "OK",
                                relative_pos: 0.0)
                        };

                        await mqttPublisher.PublishCycleTimeAsync(
                            lineId: chain.Name,
                            calculatedCtSeconds: ctSeconds,
                            isRunning: true,
                            encoderDelta: 0.0,
                            jigs: jigs,
                            cancellationToken: cancellationToken);
                        publishedCycleTime = true;
                    }

                    if (!publishedCycleTime)
                    {
                        _ = raspberryPi.SendSpeedCommandAsync(
                            chain.Name,
                            recommendation.RecommendedSpeedRpm,
                            "auto",
                            cancellationToken);
                    }
                }
            }
        }

        return Ok(new ScanIngestResponse(scan.Id, recommendation?.RecommendedSpeedRpm, recommendation?.Confidence, recommendation?.Rationale));
    }

    private async Task<SpeedRecommendationResult?> ComputeHeijunkaRecommendationAsync(
        Guid chainId,
        string? foName,
        CancellationToken cancellationToken)
    {
        var chain = await db.Chains
            .AsNoTracking()
            .Include(x => x.Tables)
            .FirstOrDefaultAsync(x => x.Id == chainId, cancellationToken);

        if (chain is null)
        {
            return null;
        }

        var batch = await db.FoBatches.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ChainId == chainId, cancellationToken);

        if (batch is null)
        {
            return null;
        }

        var foHarnesses = await db.FoHarnesses.AsNoTracking()
            .Where(x => x.FoBatchId == batch.Id)
            .ToListAsync(cancellationToken);

        var boardCount = Math.Max(0, chain.Tables.Count);
        var activeHarnesses = foHarnesses
            .OrderBy(x => x.OrderIndex)
            .Take(boardCount)
            .ToList();
        var manHours = activeHarnesses.Select(x => x.ProductionTimeMinutes).ToList();

        var recommendation = heijunka.Recommend(
            manHours,
            chain.WorkerCount,
            chain.ProductivityFactor,
            chain.PitchDistanceMeters,
            chain.BalancingTuningK);

        batch.RecommendedSpeedRpm = recommendation.RecommendedSpeedRpm;
        db.FoBatches.Update(batch);
        await db.SaveChangesAsync(cancellationToken);

        return recommendation;
    }
}

public sealed record ScanIngestRequest(
    string Barcode,
    string? ScannerId,
    Guid? ChainId,
    Guid? TableId,
    DateTimeOffset? ScannedAtUtc,
    string? Fo,
    string? HarnessType,
    int? Quantity,
    DateOnly? PlannedDate,
    int? ProductionTimeInMinutes,
    bool? IsUrgent);

public sealed record ScanIngestResponse(Guid ScanId, double? RecommendedSpeedRpm, double? Confidence, string? Rationale);
