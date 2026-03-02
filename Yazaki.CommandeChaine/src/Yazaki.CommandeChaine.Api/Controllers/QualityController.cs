using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Yazaki.CommandeChaine.Api.Hubs;
using Yazaki.CommandeChaine.Core.Entities.Events;
using Yazaki.CommandeChaine.Infrastructure.Persistence;

namespace Yazaki.CommandeChaine.Api.Controllers;

[ApiController]
[Route("api/quality")]
public sealed class QualityController(CommandeChaineDbContext db, IHubContext<RealtimeHub> hub) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult> Add([FromBody] QualityIngestRequest request, CancellationToken cancellationToken)
    {
        if (request.ChainId == Guid.Empty)
        {
            return BadRequest("ChainId is required");
        }

        var now = DateTimeOffset.UtcNow;
        var ev = new QualityEvent
        {
            Id = Guid.NewGuid(),
            ChainId = request.ChainId,
            TableId = request.TableId,
            Kind = request.Kind,
            Cause = ParseCause(request.Cause),
            DelayPercent = request.DelayPercent,
            DurationMinutes = request.DurationMinutes,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            OccurredAtUtc = request.OccurredAtUtc ?? now,
            CreatedAtUtc = now
        };

        db.QualityEvents.Add(ev);
        await db.SaveChangesAsync(cancellationToken);

        await hub.Clients.Group(RealtimeHub.ChainGroup(request.ChainId)).SendAsync("QualityEvent", new
        {
            ev.Id,
            ev.ChainId,
            ev.TableId,
            ev.Kind,
            ev.Cause,
            ev.DelayPercent,
            ev.DurationMinutes,
            ev.Note,
            ev.OccurredAtUtc
        }, cancellationToken);

        return Ok();
    }

    private static QualityEventCause? ParseCause(string? cause)
    {
        if (string.IsNullOrWhiteSpace(cause))
        {
            return null;
        }

        return cause.Trim().ToLowerInvariant() switch
        {
            "retard" => QualityEventCause.Retard,
            "panne" => QualityEventCause.Panne,
            "qualite" => QualityEventCause.Qualite,
            "autre" => QualityEventCause.Autre,
            _ => null
        };
    }
}

public sealed record QualityIngestRequest(Guid ChainId, Guid? TableId, QualityEventKind Kind, string? Cause, double? DelayPercent, double? DurationMinutes, string? Note, DateTimeOffset? OccurredAtUtc);
