using Microsoft.EntityFrameworkCore;
using Yazaki.CommandeChaine.Core.Entities.Cables;
using Yazaki.CommandeChaine.Core.Entities.Chains;
using Yazaki.CommandeChaine.Core.Entities.Events;
using Yazaki.CommandeChaine.Core.Entities.Fo;
using Yazaki.CommandeChaine.Core.Entities.Speeds;

namespace Yazaki.CommandeChaine.Infrastructure.Persistence;

public sealed class CommandeChaineDbContext(DbContextOptions<CommandeChaineDbContext> options) : DbContext(options)
{
    public DbSet<Chain> Chains => Set<Chain>();
    public DbSet<ChainTable> ChainTables => Set<ChainTable>();

    public DbSet<CableCategory> CableCategories => Set<CableCategory>();
    public DbSet<CableReference> CableReferences => Set<CableReference>();
    public DbSet<SpeedRule> SpeedRules => Set<SpeedRule>();

    public DbSet<FoBatch> FoBatches => Set<FoBatch>();
    public DbSet<FoHarness> FoHarnesses => Set<FoHarness>();
    public DbSet<BoardCableValidation> BoardCableValidations => Set<BoardCableValidation>();

    public DbSet<BarcodeScanEvent> BarcodeScanEvents => Set<BarcodeScanEvent>();
    public DbSet<QualityEvent> QualityEvents => Set<QualityEvent>();
    public DbSet<TimeCreditHistory> TimeCreditHistory => Set<TimeCreditHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Chain>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.WorkerCount).HasDefaultValue(1);
            entity.Property(x => x.ProductivityFactor).HasDefaultValue(1.0);
            entity.Property(x => x.PitchDistanceMeters).HasDefaultValue(1.0);
            entity.Property(x => x.BalancingTuningK).HasDefaultValue(0.7);
            entity.HasMany(x => x.Tables).WithOne(x => x.Chain).HasForeignKey(x => x.ChainId);
        });

        modelBuilder.Entity<ChainTable>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.TimeCreditMinutes).HasDefaultValue(0);
            entity.Property(x => x.TimeCreditRatio).HasDefaultValue(0);
            entity.Property(x => x.TimeCreditTargetRatio).HasDefaultValue(0);
            entity.HasIndex(x => new { x.ChainId, x.Index }).IsUnique();
        });

        modelBuilder.Entity<CableCategory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(50);
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<CableReference>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Reference).HasMaxLength(200);
            entity.HasIndex(x => x.Reference).IsUnique();
        });

        modelBuilder.Entity<SpeedRule>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CategoryCode).HasMaxLength(50);
            entity.HasIndex(x => x.CategoryCode).IsUnique();
        });

        modelBuilder.Entity<FoBatch>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FoName).HasMaxLength(200);
            entity.HasIndex(x => x.ChainId).IsUnique();
            entity.HasMany(x => x.Harnesses).WithOne(x => x.FoBatch).HasForeignKey(x => x.FoBatchId);
        });

        modelBuilder.Entity<FoHarness>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Reference).HasMaxLength(200);
            entity.HasIndex(x => x.Reference);
            entity.HasIndex(x => new { x.FoBatchId, x.OrderIndex });
        });

        modelBuilder.Entity<BoardCableValidation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.FoHarness).WithMany().HasForeignKey(x => x.FoHarnessId);
            entity.HasIndex(x => x.ChainTableId);
            entity.HasIndex(x => new { x.ChainTableId, x.Status });
            entity.HasIndex(x => x.StartedAtUtc);
            entity.Property(x => x.Status).HasConversion<int>();
        });

        modelBuilder.Entity<BarcodeScanEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Barcode).HasMaxLength(256);
            entity.Property(x => x.ScannerId).HasMaxLength(128);
            entity.Property(x => x.FoName).HasMaxLength(200);
            entity.Property(x => x.HarnessType).HasMaxLength(40);
            entity.HasIndex(x => x.ScannedAtUtc);
            entity.HasIndex(x => x.Barcode);
        });

        modelBuilder.Entity<QualityEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.HasIndex(x => x.OccurredAtUtc);
            entity.HasIndex(x => new { x.ChainId, x.OccurredAtUtc });
        });

        modelBuilder.Entity<TimeCreditHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OccurredAtUtc);
            entity.HasIndex(x => new { x.ChainId, x.OccurredAtUtc });
            entity.HasIndex(x => new { x.TableId, x.OccurredAtUtc });
        });
    }
}
