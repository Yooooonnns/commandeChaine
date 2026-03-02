using Yazaki.CommandeChaine.Core.Entities.Speeds;

namespace Yazaki.CommandeChaine.Application.Services.SpeedOptimization;

public sealed class SpeedRecommendationService
{
    public SpeedRecommendationResult Recommend(
        SpeedRule rule,
        IReadOnlyList<double> cycleTimesSeconds,
        int defectCount,
        int qualityCount)
    {
        var baseSpeed = rule.BaseSpeedRpm;
        var minSpeed = rule.MinSpeedRpm;
        var maxSpeed = rule.MaxSpeedRpm;
        var safeQualityCount = Math.Max(1, qualityCount);
        var defectRate = Math.Clamp(defectCount / (double)safeQualityCount, 0.0, 1.0);

        if (cycleTimesSeconds.Count < 5)
        {
            var confidenceLow = Math.Min(1.0, cycleTimesSeconds.Count / 20.0);
            var recommendedSpeedLow = Clamp(baseSpeed * (1.0 - 0.8 * defectRate), minSpeed, maxSpeed);

            return new SpeedRecommendationResult(
                RecommendedSpeedRpm: recommendedSpeedLow,
                Confidence: confidenceLow,
                Rationale: "Pas assez d'historique de cycle (moins de 5 cycles valides). Application de la base + pénalité qualité."
            );
        }

        var sorted = cycleTimesSeconds.OrderBy(x => x).ToArray();
        var median = PercentileSorted(sorted, 0.50);
        var p20 = PercentileSorted(sorted, 0.20);

        // If we are slower than the best recent performance, we can increase speed slightly (bounded).
        // If quality is degraded, we reduce speed.
        var performanceFactor = Clamp(p20 / median, 0.85, 1.15);
        var qualityFactor = Clamp(1.0 - 0.8 * defectRate, 0.60, 1.05);

        var recommended = baseSpeed * (1.0 / performanceFactor) * qualityFactor;
        recommended = Clamp(recommended, minSpeed, maxSpeed);

        var confidence = Clamp(Math.Min(1.0, sorted.Length / 60.0) * (1.0 - Math.Min(0.7, defectRate)), 0.05, 1.0);
        var rationale = $"Cycle médian={median:0.0}s, p20={p20:0.0}s, défauts={defectCount}/{qualityCount} (taux={defectRate:P0}).";

        return new SpeedRecommendationResult(recommended, confidence, rationale);
    }

    private static double Clamp(double value, double min, double max)
        => value < min ? min : (value > max ? max : value);

    private static double PercentileSorted(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var rank = percentile * (sorted.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        if (low == high)
        {
            return sorted[low];
        }

        var weight = rank - low;
        return sorted[low] * (1 - weight) + sorted[high] * weight;
    }
}

public sealed record SpeedRecommendationResult(double RecommendedSpeedRpm, double Confidence, string Rationale, double? CycleTimeMinutes = null);
