namespace Yazaki.CommandeChaine.Core.Entities.Fo;

public enum CompletionMode
{
    Manual = 0,
    Auto = 1
}

public sealed class FoBatch
{
    public Guid Id { get; set; }
    public Guid ChainId { get; set; }
    public string FoName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public double RecommendedSpeedRpm { get; set; }
    public CompletionMode CompletionMode { get; set; } = CompletionMode.Manual;

    public List<FoHarness> Harnesses { get; set; } = new();
}
