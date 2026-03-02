namespace Yazaki.CommandeChaine.Core.Entities.Speeds;

public sealed class SpeedRule
{
    public Guid Id { get; set; }

    public Guid CategoryId { get; set; }
    public string CategoryCode { get; set; } = string.Empty; // denormalized for quick lookups

    public double BaseSpeedRpm { get; set; }
    public double MinSpeedRpm { get; set; }
    public double MaxSpeedRpm { get; set; }
}
