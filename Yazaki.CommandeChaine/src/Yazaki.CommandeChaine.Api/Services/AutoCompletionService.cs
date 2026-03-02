using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Yazaki.CommandeChaine.Api.Hubs;
using Yazaki.CommandeChaine.Application.Services.SpeedOptimization;
using Yazaki.CommandeChaine.Core.Entities.Fo;
using Yazaki.CommandeChaine.Infrastructure.Persistence;

namespace Yazaki.CommandeChaine.Api.Services;

/// <summary>
/// Background service that automatically completes harnesses in Auto mode.
/// 
/// Phase 1 (Filling): Fill the chain up to capacity (= number of boards)
/// Phase 2 (Running): Every 10 seconds, exit first harness and enter next one.
/// 
/// CT is calculated based on ALL harnesses currently on the chain (the "configuration").
/// When a harness exits and another enters, the configuration changes and CT is recalculated.
/// </summary>
public sealed class AutoCompletionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoCompletionService> _logger;
    private const int IntervalSeconds = 10; // Fixed 10 second interval for testing

    public AutoCompletionService(IServiceProvider serviceProvider, ILogger<AutoCompletionService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("\n========================================\nAutoCompletionService STARTED\nInterval: {Interval} seconds\n========================================", IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAutoCompletionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "\n[ERROR] AutoCompletionService error");
            }

            await Task.Delay(TimeSpan.FromSeconds(IntervalSeconds), stoppingToken);
        }
    }

    private async Task ProcessAutoCompletionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommandeChaineDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<RealtimeHub>>();
        var heijunka = scope.ServiceProvider.GetRequiredService<HeijunkaLevelingService>();
        var mqttPublisher = scope.ServiceProvider.GetRequiredService<MqttCycleTimePublisher>();

        // Get all batches in Auto mode
        var autoBatches = await db.FoBatches
            .Include(x => x.Harnesses)
            .Where(x => x.CompletionMode == CompletionMode.Auto)
            .ToListAsync(cancellationToken);

        if (autoBatches.Count == 0) return;

        foreach (var batch in autoBatches)
        {
            var chain = await db.Chains
                .Include(x => x.Tables)
                .FirstOrDefaultAsync(x => x.Id == batch.ChainId, cancellationToken);

            if (chain is null) continue;

            var boardCount = chain.Tables.Count;
            var maxOnChain = boardCount; // Chain capacity = number of boards
            var now = DateTimeOffset.UtcNow;

            // Get current state
            var onChainHarnesses = batch.Harnesses
                .Where(x => x.IsOnChain && !x.IsCompleted)
                .OrderBy(x => x.ChainPosition)
                .ToList();

            var pendingHarnesses = batch.Harnesses
                .Where(x => !x.IsOnChain && !x.IsCompleted)
                .OrderBy(x => x.OrderIndex)
                .ToList();

            var completedCount = batch.Harnesses.Count(x => x.IsCompleted);

            // Check if FO is complete
            if (onChainHarnesses.Count == 0 && pendingHarnesses.Count == 0)
            {
                _logger.LogInformation("\n[FO COMPLETE] All harnesses completed for FO: {FoName}\n  Total: {Total}", 
                    batch.FoName, batch.Harnesses.Count);
                continue;
            }

            bool configurationChanged = false;

            // PHASE 1: FILLING - If chain not at capacity, fill it up
            if (onChainHarnesses.Count < maxOnChain && pendingHarnesses.Count > 0)
            {
                var toFill = Math.Min(maxOnChain - onChainHarnesses.Count, pendingHarnesses.Count);
                
                for (int i = 0; i < toFill; i++)
                {
                    var harness = pendingHarnesses[i];
                    harness.IsOnChain = true;
                    harness.EnteredChainAtUtc = now;
                    harness.ChainPosition = onChainHarnesses.Count + i;

                    _logger.LogInformation("\n[HARNESS ENTRY] >>>> {Reference} entered chain\n  FO: {FoName}\n  Chain: {ChainName}\n  Position: {Position}/{MaxOnChain}",
                        harness.Reference, batch.FoName, chain.Name, harness.ChainPosition + 1, maxOnChain);
                }

                configurationChanged = true;
                await db.SaveChangesAsync(cancellationToken);

                // Refresh on-chain list
                onChainHarnesses = batch.Harnesses
                    .Where(x => x.IsOnChain && !x.IsCompleted)
                    .OrderBy(x => x.ChainPosition)
                    .ToList();
                
                pendingHarnesses = batch.Harnesses
                    .Where(x => !x.IsOnChain && !x.IsCompleted)
                    .OrderBy(x => x.OrderIndex)
                    .ToList();
            }
            // PHASE 2: RUNNING - Chain is full, exit first and enter next
            else if (onChainHarnesses.Count > 0)
            {
                // EXIT: First harness leaves the chain
                var exitingHarness = onChainHarnesses.First();
                exitingHarness.IsOnChain = false;
                exitingHarness.IsCompleted = true;
                exitingHarness.CompletedAtUtc = now;

                // Shift positions of remaining harnesses
                foreach (var h in onChainHarnesses.Skip(1))
                {
                    h.ChainPosition--;
                }

                completedCount = batch.Harnesses.Count(x => x.IsCompleted);
                _logger.LogInformation("\n[HARNESS EXIT] <<<< {Reference} exited chain\n  FO: {FoName}\n  Chain: {ChainName}\n  Progress: {Completed}/{Total}",
                    exitingHarness.Reference, batch.FoName, chain.Name, completedCount, batch.Harnesses.Count);

                // Broadcast harness completed
                await hubContext.Clients.Group(RealtimeHub.ChainGroup(batch.ChainId))
                    .SendAsync("HarnessCompleted", new
                    {
                        ChainId = batch.ChainId,
                        Reference = exitingHarness.Reference,
                        CompletedCount = completedCount,
                        TotalCount = batch.Harnesses.Count
                    }, cancellationToken);

                // ENTER: Next pending harness enters
                var nextHarness = pendingHarnesses.FirstOrDefault();
                if (nextHarness is not null)
                {
                    nextHarness.IsOnChain = true;
                    nextHarness.EnteredChainAtUtc = now;
                    nextHarness.ChainPosition = onChainHarnesses.Count - 1; // Last position (after shift)

                    _logger.LogInformation("\n[HARNESS ENTRY] >>>> {Reference} entered chain\n  FO: {FoName}\n  Chain: {ChainName}\n  Position: {Position}/{MaxOnChain}",
                        nextHarness.Reference, batch.FoName, chain.Name, nextHarness.ChainPosition + 1, maxOnChain);
                }

                configurationChanged = true;
                await db.SaveChangesAsync(cancellationToken);

                // Refresh on-chain list
                onChainHarnesses = batch.Harnesses
                    .Where(x => x.IsOnChain && !x.IsCompleted)
                    .OrderBy(x => x.ChainPosition)
                    .ToList();
            }

            // RECALCULATE CT when configuration changes
            if (configurationChanged && onChainHarnesses.Count > 0)
            {
                var manHours = onChainHarnesses.Select(x => x.ProductionTimeMinutes).ToList();
                var recommendation = heijunka.Recommend(
                    manHours,
                    chain.WorkerCount,
                    chain.ProductivityFactor,
                    chain.PitchDistanceMeters,
                    chain.BalancingTuningK);

                var ctMinutes = recommendation.CycleTimeMinutes ?? 0;
                var ctSeconds = Math.Round(ctMinutes * 60.0, 3);
                
                _logger.LogInformation("\n[CT UPDATE] Configuration changed - Recalculated CT\n  Chain: {ChainName}\n  CT: {CT:0.0} min ({CTSec} sec)\n  Harnesses on chain: {Count}/{MaxOnChain}\n  Configuration: [{Refs}]",
                    chain.Name, ctMinutes, ctSeconds, onChainHarnesses.Count, maxOnChain, 
                    string.Join(", ", onChainHarnesses.Select(h => $"{h.Reference}({h.ProductionTimeMinutes}min)")));

                // Publish CT to Raspberry via MQTT
                if (ctMinutes > 0)
                {
                    var jigs = onChainHarnesses.Select((h, idx) => new MqttJigPayload(
                        jig_id: h.Reference,
                        status: "ON_CHAIN",
                        relative_pos: idx / (double)Math.Max(1, onChainHarnesses.Count)
                    )).ToList();

                    await mqttPublisher.PublishCycleTimeAsync(
                        lineId: chain.Name,
                        calculatedCtSeconds: ctSeconds,
                        isRunning: true,
                        encoderDelta: 0.0,
                        jigs: jigs,
                        cancellationToken: cancellationToken);

                    _logger.LogInformation("\n[MQTT] Sent CT to Raspberry: yazaki/line/{LineId}/ct -> {CT} seconds", chain.Name, ctSeconds);
                }

                // SIMULATE Raspberry response (for testing when MQTT broker is unavailable)
                // In production, this comes from MqttSpeedSubscriber when Raspberry responds
                // Formula: speed_rpm = pitchDistance / (CT_seconds / 60) -- simplified simulation
                var simulatedSpeedRpm = chain.PitchDistanceMeters > 0 && ctSeconds > 0
                    ? Math.Round(chain.PitchDistanceMeters / (ctSeconds / 60.0), 4)
                    : 0.0;
                var simulatedVoltage = simulatedSpeedRpm * 0.1; // Simulated voltage

                _logger.LogInformation("\n[SIMULATED RASPBERRY] Speed response (MQTT broker unavailable)\n  Chain: {ChainName}\n  CT: {CT} sec -> Speed: {Speed:0.0000} RPM\n  Voltage: {Voltage:0.00}V",
                    chain.Name, ctSeconds, simulatedSpeedRpm, simulatedVoltage);

                // Update batch with simulated speed
                batch.RecommendedSpeedRpm = simulatedSpeedRpm;
                await db.SaveChangesAsync(cancellationToken);

                // Broadcast to Desktop - simulating what Raspberry would send back
                await hubContext.Clients.Group(RealtimeHub.ChainGroup(batch.ChainId))
                    .SendAsync("SpeedFromRaspberry", new
                    {
                        ChainId = batch.ChainId,
                        LineId = chain.Name,
                        SpeedRpm = simulatedSpeedRpm,
                        Voltage = simulatedVoltage,
                        CtSeconds = ctSeconds,
                        HarnessesOnChain = onChainHarnesses.Count,
                        Timestamp = DateTimeOffset.UtcNow.ToString("o")
                    }, cancellationToken);
            }
        }
    }
}
