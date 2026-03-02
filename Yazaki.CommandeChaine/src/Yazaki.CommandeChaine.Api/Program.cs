using Microsoft.EntityFrameworkCore;
using Yazaki.CommandeChaine.Application.Services.SpeedOptimization;
using Yazaki.CommandeChaine.Core.Entities.Cables;
using Yazaki.CommandeChaine.Core.Entities.Chains;
using Yazaki.CommandeChaine.Core.Entities.Speeds;
using Yazaki.CommandeChaine.Infrastructure;
using Yazaki.CommandeChaine.Infrastructure.Persistence;
using Yazaki.CommandeChaine.Api.Hubs;
using Yazaki.CommandeChaine.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSignalR();

builder.Services.AddCommandeChaineInfrastructure(builder.Configuration);
builder.Services.AddScoped<SpeedRecommendationService>();
builder.Services.AddScoped<HeijunkaLevelingService>();
builder.Services.AddSingleton<MqttCycleTimePublisher>();
builder.Services.AddHostedService<MqttSpeedSubscriber>();
builder.Services.AddHostedService<AutoCompletionService>();

// Raspberry Pi integration
builder.Services.AddHttpClient<RaspberryPiClient>()
    .ConfigureHttpClient((sp, client) =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var raspberryPiUrl = config["RaspberryPi:ApiUrl"];
        if (!string.IsNullOrWhiteSpace(raspberryPiUrl))
        {
            client.BaseAddress = new Uri(raspberryPiUrl.EndsWith('/') ? raspberryPiUrl : raspberryPiUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(5);
        }
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHub<RealtimeHub>("/hubs/realtime");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Raspberry Pi health check endpoint
app.MapGet("/api/raspberrypi/health", async (RaspberryPiClient raspberryPi) =>
{
    var isHealthy = await raspberryPi.IsHealthyAsync();
    return Results.Ok(new
    {
        isHealthy = isHealthy,
        status = isHealthy ? "healthy" : "offline",
        lastVoltage = raspberryPi.LastVoltage,
        lastCommandAtUtc = raspberryPi.LastCommandAtUtc,
        chainRunning = raspberryPi.LastChainRunning,
        encoderDelta = raspberryPi.LastEncoderDelta
    });
});

await SeedAsync(app.Services);

app.Run();

static async Task SeedAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CommandeChaineDbContext>();
    await db.Database.EnsureCreatedAsync();
    await EnsureFoSchemaAsync(db);
    await EnsureQualitySchemaAsync(db);
    await EnsureCreditSchemaAsync(db);
    await EnsureScanSchemaAsync(db);
    await EnsureChainSchemaAsync(db);

    if (!await db.CableCategories.AnyAsync())
    {
        db.CableCategories.AddRange(
            new CableCategory { Id = Guid.NewGuid(), Code = "charge", DisplayName = "Chargé" },
            new CableCategory { Id = Guid.NewGuid(), Code = "moyen", DisplayName = "Moyen" },
            new CableCategory { Id = Guid.NewGuid(), Code = "leger", DisplayName = "Léger" }
        );

        await db.SaveChangesAsync();
    }

    if (!await db.SpeedRules.AnyAsync())
    {
        var categories = await db.CableCategories.ToListAsync();
        var charge = categories.FirstOrDefault(x => x.Code == "charge") ?? new CableCategory { Id = Guid.NewGuid(), Code = "charge", DisplayName = "Chargé" };
        var moyen = categories.FirstOrDefault(x => x.Code == "moyen") ?? new CableCategory { Id = Guid.NewGuid(), Code = "moyen", DisplayName = "Moyen" };
        var leger = categories.FirstOrDefault(x => x.Code == "leger") ?? new CableCategory { Id = Guid.NewGuid(), Code = "leger", DisplayName = "Léger" };

        db.SpeedRules.AddRange(
            new SpeedRule { Id = Guid.NewGuid(), CategoryId = charge.Id, CategoryCode = charge.Code, BaseSpeedRpm = 25, MinSpeedRpm = 10, MaxSpeedRpm = 40 },
            new SpeedRule { Id = Guid.NewGuid(), CategoryId = moyen.Id, CategoryCode = moyen.Code, BaseSpeedRpm = 35, MinSpeedRpm = 15, MaxSpeedRpm = 60 },
            new SpeedRule { Id = Guid.NewGuid(), CategoryId = leger.Id, CategoryCode = leger.Code, BaseSpeedRpm = 45, MinSpeedRpm = 20, MaxSpeedRpm = 80 }
        );
    }

    if (!await db.CableReferences.AnyAsync())
    {
        var categories = await db.CableCategories.ToListAsync();
        var charge = categories.FirstOrDefault(x => x.Code == "charge")!;
        var moyen = categories.FirstOrDefault(x => x.Code == "moyen")!;
        var leger = categories.FirstOrDefault(x => x.Code == "leger")!;

        db.CableReferences.AddRange(
            new CableReference { Id = Guid.NewGuid(), Reference = "REF-CHARGE-001", CategoryId = charge.Id },
            new CableReference { Id = Guid.NewGuid(), Reference = "REF-MOYEN-001", CategoryId = moyen.Id },
            new CableReference { Id = Guid.NewGuid(), Reference = "REF-LEGER-001", CategoryId = leger.Id }
        );
    }

    if (!await db.Chains.AnyAsync())
    {
        var chain = new Chain
        {
            Id = Guid.NewGuid(),
            Name = "Chaine-01",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            WorkerCount = 12,
            ProductivityFactor = 1.0,
            PitchDistanceMeters = 1.0,
            BalancingTuningK = 0.7,
            Tables = new List<ChainTable>()
        };
        db.Chains.Add(chain);
    }

    await db.SaveChangesAsync();
}

static async Task EnsureFoSchemaAsync(CommandeChaineDbContext db)
{
    await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS FoBatches (
    Id TEXT PRIMARY KEY,
    ChainId TEXT NOT NULL,
    FoName TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    RecommendedSpeedRpm REAL NOT NULL DEFAULT 0
);");

    await db.Database.ExecuteSqlRawAsync(@"
CREATE UNIQUE INDEX IF NOT EXISTS IX_FoBatches_ChainId ON FoBatches(ChainId);");

    await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS FoHarnesses (
    Id TEXT PRIMARY KEY,
    FoBatchId TEXT NOT NULL,
    Reference TEXT NOT NULL,
    ProductionTimeMinutes INTEGER NOT NULL,
    IsUrgent INTEGER NOT NULL,
    IsLate INTEGER NOT NULL,
    OrderIndex INTEGER NOT NULL,
    IsCompleted INTEGER NOT NULL DEFAULT 0,
    CompletedAtUtc TEXT NULL,
    FOREIGN KEY(FoBatchId) REFERENCES FoBatches(Id) ON DELETE CASCADE
);");

    await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS IX_FoHarnesses_Reference ON FoHarnesses(Reference);");

    await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS IX_FoHarnesses_FoBatchId_OrderIndex ON FoHarnesses(FoBatchId, OrderIndex);");

    await TryAddColumnAsync(db, "FoBatches", "RecommendedSpeedRpm REAL NOT NULL DEFAULT 0");
    await TryAddColumnAsync(db, "FoBatches", "CompletionMode INTEGER NOT NULL DEFAULT 0");
    await TryAddColumnAsync(db, "FoHarnesses", "IsCompleted INTEGER NOT NULL DEFAULT 0");
    await TryAddColumnAsync(db, "FoHarnesses", "CompletedAtUtc TEXT NULL");
    await TryAddColumnAsync(db, "FoHarnesses", "IsOnChain INTEGER NOT NULL DEFAULT 0");
    await TryAddColumnAsync(db, "FoHarnesses", "EnteredChainAtUtc TEXT NULL");
    await TryAddColumnAsync(db, "FoHarnesses", "ChainPosition INTEGER NOT NULL DEFAULT 0");
}

static async Task EnsureQualitySchemaAsync(CommandeChaineDbContext db)
{
    await TryAddColumnAsync(db, "QualityEvents", "Cause INTEGER NULL");
    await TryAddColumnAsync(db, "QualityEvents", "DelayPercent REAL NULL");
    await TryAddColumnAsync(db, "QualityEvents", "DurationMinutes REAL NULL");
}

static async Task EnsureCreditSchemaAsync(CommandeChaineDbContext db)
{
    await TryAddColumnAsync(db, "ChainTables", "TimeCreditMinutes REAL NOT NULL DEFAULT 0");
    await TryAddColumnAsync(db, "ChainTables", "TimeCreditRatio REAL NOT NULL DEFAULT 0");
    await TryAddColumnAsync(db, "ChainTables", "TimeCreditTargetRatio REAL NOT NULL DEFAULT 0");
    await TryAddColumnAsync(db, "ChainTables", "TimeCreditUpdatedAtUtc TEXT NULL");

    await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS TimeCreditHistory (
    Id TEXT PRIMARY KEY,
    ChainId TEXT NOT NULL,
    TableId TEXT NOT NULL,
    ProgressRatio REAL NOT NULL,
    TargetRatio REAL NOT NULL,
    CreditRatio REAL NOT NULL,
    CreditMinutes REAL NOT NULL,
    OccurredAtUtc TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL
);");

    await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS IX_TimeCreditHistory_OccurredAtUtc ON TimeCreditHistory(OccurredAtUtc);");

    await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS IX_TimeCreditHistory_ChainId_OccurredAtUtc ON TimeCreditHistory(ChainId, OccurredAtUtc);");

    await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS IX_TimeCreditHistory_TableId_OccurredAtUtc ON TimeCreditHistory(TableId, OccurredAtUtc);");
}

static async Task EnsureScanSchemaAsync(CommandeChaineDbContext db)
{
    await TryAddColumnAsync(db, "BarcodeScanEvents", "FoName TEXT NULL");
    await TryAddColumnAsync(db, "BarcodeScanEvents", "HarnessType TEXT NULL");
    await TryAddColumnAsync(db, "BarcodeScanEvents", "ProductionTimeMinutes INTEGER NULL");
    await TryAddColumnAsync(db, "BarcodeScanEvents", "IsUrgent INTEGER NULL");
}

static async Task EnsureChainSchemaAsync(CommandeChaineDbContext db)
{
    await TryAddColumnAsync(db, "Chains", "WorkerCount INTEGER NOT NULL DEFAULT 1");
    await TryAddColumnAsync(db, "Chains", "ProductivityFactor REAL NOT NULL DEFAULT 1.0");
    await TryAddColumnAsync(db, "Chains", "PitchDistanceMeters REAL NOT NULL DEFAULT 1.0");
    await TryAddColumnAsync(db, "Chains", "BalancingTuningK REAL NOT NULL DEFAULT 0.7");
}


static async Task TryAddColumnAsync(CommandeChaineDbContext db, string table, string columnDef)
{
    try
    {
        var allowedTables = new HashSet<string>(StringComparer.Ordinal)
        {
            "FoBatches",
            "FoHarnesses",
            "QualityEvents",
            "ChainTables",
            "BarcodeScanEvents",
            "Chains"
        };

        var allowedColumnDefs = new HashSet<string>(StringComparer.Ordinal)
        {
            "RecommendedSpeedRpm REAL NOT NULL DEFAULT 0",
            "CompletionMode INTEGER NOT NULL DEFAULT 0",
            "IsCompleted INTEGER NOT NULL DEFAULT 0",
            "CompletedAtUtc TEXT NULL",
            "IsOnChain INTEGER NOT NULL DEFAULT 0",
            "EnteredChainAtUtc TEXT NULL",
            "ChainPosition INTEGER NOT NULL DEFAULT 0",
            "Cause INTEGER NULL",
            "DelayPercent REAL NULL",
            "DurationMinutes REAL NULL",
            "TimeCreditMinutes REAL NOT NULL DEFAULT 0",
            "TimeCreditRatio REAL NOT NULL DEFAULT 0",
            "TimeCreditTargetRatio REAL NOT NULL DEFAULT 0",
            "TimeCreditUpdatedAtUtc TEXT NULL",
            "FoName TEXT NULL",
            "HarnessType TEXT NULL",
            "ProductionTimeMinutes INTEGER NULL",
            "IsUrgent INTEGER NULL",
            "WorkerCount INTEGER NOT NULL DEFAULT 1",
            "ProductivityFactor REAL NOT NULL DEFAULT 1.0",
            "PitchDistanceMeters REAL NOT NULL DEFAULT 1.0",
            "BalancingTuningK REAL NOT NULL DEFAULT 0.7"
        };

        if (!allowedTables.Contains(table) || !allowedColumnDefs.Contains(columnDef))
        {
            throw new InvalidOperationException("Unsupported schema change.");
        }

        var sql = "ALTER TABLE " + table + " ADD COLUMN " + columnDef + ";";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
    catch
    {
        // Ignore if column already exists.
    }
}
