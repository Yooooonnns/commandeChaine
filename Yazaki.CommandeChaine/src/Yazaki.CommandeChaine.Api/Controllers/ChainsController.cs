using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yazaki.CommandeChaine.Core.Entities.Chains;
using Yazaki.CommandeChaine.Infrastructure.Persistence;

namespace Yazaki.CommandeChaine.Api.Controllers;

[ApiController]
[Route("api/chains")]
public sealed class ChainsController(CommandeChaineDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ChainDto>>> GetAll(CancellationToken cancellationToken)
    {
        var chains = await db.Chains
            .AsNoTracking()
            .Include(x => x.Tables)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return chains.Select(x => new ChainDto(
            x.Id,
            x.Name,
            x.WorkerCount,
            x.ProductivityFactor,
            x.PitchDistanceMeters,
            x.BalancingTuningK,
            x.Tables.OrderBy(t => t.Index).Select(t => new ChainTableDto(t.Id, t.Index, t.Name)).ToList()))
            .ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChainDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var chain = await db.Chains
            .AsNoTracking()
            .Include(x => x.Tables)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (chain is null)
        {
            return NotFound();
        }

        return new ChainDto(
            chain.Id,
            chain.Name,
            chain.WorkerCount,
            chain.ProductivityFactor,
            chain.PitchDistanceMeters,
            chain.BalancingTuningK,
            chain.Tables.OrderBy(t => t.Index).Select(t => new ChainTableDto(t.Id, t.Index, t.Name)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ChainDto>> Create([FromBody] CreateChainRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required");
        }

        var tableCount = Math.Max(0, request.TableCount);

        var chain = new Chain
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            WorkerCount = Math.Max(1, request.WorkerCount),
            ProductivityFactor = Math.Max(0.01, request.ProductivityFactor),
            PitchDistanceMeters = Math.Max(0.001, request.PitchDistanceMeters),
            BalancingTuningK = Math.Max(0.0, request.BalancingTuningK),
            Tables = tableCount == 0
                ? new List<ChainTable>()
                : Enumerable.Range(1, tableCount)
                    .Select(i => new ChainTable
                    {
                        Id = Guid.NewGuid(),
                        Index = i,
                        Name = $"Tableau {i}",
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    })
                    .ToList()
        };

        db.Chains.Add(chain);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { id = chain.Id }, new ChainDto(
            chain.Id,
            chain.Name,
            chain.WorkerCount,
            chain.ProductivityFactor,
            chain.PitchDistanceMeters,
            chain.BalancingTuningK,
            chain.Tables.Select(t => new ChainTableDto(t.Id, t.Index, t.Name)).ToList()));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ChainDto>> Rename(Guid id, [FromBody] RenameChainRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required");
        }

        var chain = await db.Chains
            .Include(x => x.Tables)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (chain is null)
        {
            return NotFound();
        }

        chain.Name = request.Name.Trim();
        await db.SaveChangesAsync(cancellationToken);

        return new ChainDto(
            chain.Id,
            chain.Name,
            chain.WorkerCount,
            chain.ProductivityFactor,
            chain.PitchDistanceMeters,
            chain.BalancingTuningK,
            chain.Tables.OrderBy(t => t.Index).Select(t => new ChainTableDto(t.Id, t.Index, t.Name)).ToList());
    }

    [HttpPut("tables/{tableId:guid}")]
    public async Task<ActionResult<ChainTableDto>> RenameTable(Guid tableId, [FromBody] RenameChainTableRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required");
        }

        var table = await db.ChainTables
            .FirstOrDefaultAsync(x => x.Id == tableId, cancellationToken);

        if (table is null)
        {
            return NotFound();
        }

        table.Name = request.Name.Trim();
        await db.SaveChangesAsync(cancellationToken);

        return new ChainTableDto(table.Id, table.Index, table.Name);
    }

    [HttpPut("{id:guid}/tables")]
    public async Task<ActionResult<ChainDto>> UpdateTables(Guid id, [FromBody] UpdateChainTablesRequest request, CancellationToken cancellationToken)
    {
        var tableCount = Math.Max(0, request.TableCount);

        var chain = await db.Chains
            .Include(x => x.Tables)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (chain is null)
        {
            return NotFound();
        }

        var ordered = chain.Tables.OrderBy(t => t.Index).ToList();
        if (tableCount > ordered.Count)
        {
            var startIndex = ordered.Count + 1;
            var additions = Enumerable.Range(startIndex, tableCount - ordered.Count)
                .Select(i => new ChainTable
                {
                    Id = Guid.NewGuid(),
                    ChainId = chain.Id,
                    Index = i,
                    Name = $"Tableau {i}",
                    CreatedAtUtc = DateTimeOffset.UtcNow
                })
                .ToList();

            db.ChainTables.AddRange(additions);
        }
        else if (tableCount < ordered.Count)
        {
            var toRemove = ordered.Skip(tableCount).ToList();
            db.ChainTables.RemoveRange(toRemove);
        }

        await db.SaveChangesAsync(cancellationToken);

        var refreshed = await db.Chains
            .AsNoTracking()
            .Include(x => x.Tables)
            .FirstAsync(x => x.Id == id, cancellationToken);

        return new ChainDto(
            refreshed.Id,
            refreshed.Name,
            refreshed.WorkerCount,
            refreshed.ProductivityFactor,
            refreshed.PitchDistanceMeters,
            refreshed.BalancingTuningK,
            refreshed.Tables.OrderBy(t => t.Index).Select(t => new ChainTableDto(t.Id, t.Index, t.Name)).ToList());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var chain = await db.Chains.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (chain is null)
        {
            return NotFound();
        }

        db.Chains.Remove(chain);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record CreateChainRequest(
    string Name,
    int TableCount,
    int WorkerCount = 1,
    double ProductivityFactor = 1.0,
    double PitchDistanceMeters = 1.0,
    double BalancingTuningK = 0.7);
public sealed record RenameChainRequest(string Name);
public sealed record RenameChainTableRequest(string Name);
public sealed record UpdateChainTablesRequest(int TableCount);
public sealed record ChainTableDto(Guid Id, int Index, string Name);
public sealed record ChainDto(
    Guid Id,
    string Name,
    int WorkerCount,
    double ProductivityFactor,
    double PitchDistanceMeters,
    double BalancingTuningK,
    List<ChainTableDto> Tables);
