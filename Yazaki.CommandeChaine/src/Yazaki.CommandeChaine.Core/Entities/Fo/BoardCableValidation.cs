namespace Yazaki.CommandeChaine.Core.Entities.Fo;

public sealed class BoardCableValidation
{
    public Guid Id { get; set; }
    
    public Guid FoHarnessId { get; set; }
    public FoHarness? FoHarness { get; set; }
    
    public Guid ChainTableId { get; set; }
    
    public BoardCableValidationStatus Status { get; set; }
    
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public enum BoardCableValidationStatus
{
    Pending = 0,
    Started = 1,
    Completed = 2
}
