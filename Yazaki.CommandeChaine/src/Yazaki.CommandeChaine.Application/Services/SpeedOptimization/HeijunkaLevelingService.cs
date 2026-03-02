namespace Yazaki.CommandeChaine.Application.Services.SpeedOptimization
{
    public sealed class HeijunkaLevelingService
    {
        public SpeedRecommendationResult Recommend(
            IReadOnlyList<int> manHoursMinutes,
            int workerCount,
            double productivityFactor,
            double pitchDistanceMeters,
            double balancingTuningK)
        {
            if (manHoursMinutes.Count == 0)
            {
                return new SpeedRecommendationResult(0.0, 0.1, "Aucune donnee de ManHour pour calculer le CT.", 0.0);
            }

            var safeWorkers = Math.Max(1, workerCount);

            var avg = manHoursMinutes.Average();
            var ctMinutes = avg > 0
                ? (avg / safeWorkers)
                : 0.0;

            var chainSpeed = ctMinutes > 0
                ? (1.0 / ctMinutes)
                : 0.0;

            var confidence = Clamp(0.3 + Math.Min(0.6, manHoursMinutes.Count / 20.0), 0.1, 0.95);
            var rationale = $"CT simplifie=MH/NOP, CT={ctMinutes:0.00}min, avgMH={avg:0.0}, NOP={safeWorkers}. Productivite fixee a 1.";

            return new SpeedRecommendationResult(chainSpeed, confidence, rationale, ctMinutes);
        }

        private static double Clamp(double value, double min, double max)
            => value < min ? min : (value > max ? max : value);
    }
}