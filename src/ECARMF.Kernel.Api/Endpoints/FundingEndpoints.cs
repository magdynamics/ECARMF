using ECARMF.Kernel.Application.Capital;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Domain.Capital;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public record SaveFundingSourceRequest(
    string SourceId, string UnitId, string Kind, string Name,
    string? Institution, decimal? CommitmentAmount, string? Notes);

public record RequestFundingEventRequest(
    string EventType, decimal Amount, string? MilestoneReference,
    decimal? PercentCompleteClaimed, string? DocumentationReference, string? VerificationNote);

public record FundingDecisionRequest(string Action, string? Comment);

/// <summary>
/// Construction funding (Rosetta Requirement 4): capital flowing INTO a
/// project. Lender draws claimed against milestones and investor capital
/// calls run through the same request → human decision → disbursement
/// chain, fully audited.
/// </summary>
public static class FundingEndpoints
{
    public static IEndpointRouteBuilder MapFundingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/funding-sources");

        group.MapGet("/", async (
            HttpContext context, IUserStore users, IFundingSourceStore sources,
            IFundingEventStore events, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            var all = await sources.GetAllAsync(tenantId, ct);
            var result = new List<object>();
            foreach (var source in all)
            {
                var sourceEvents = await events.GetBySourceAsync(tenantId, source.Id, ct);
                var funded = sourceEvents
                    .Where(e => e.Status == FundingEventStatuses.Disbursed
                        && e.EventType != FundingEventTypes.Distribution)
                    .Sum(e => e.Amount);
                result.Add(new
                {
                    source.SourceId, source.UnitId, source.Kind, source.Name,
                    source.Institution, source.CommitmentAmount, source.Notes,
                    fundedToDate = funded,
                    remainingCommitment = source.CommitmentAmount is { } c ? c - funded : (decimal?)null,
                    pendingEvents = sourceEvents.Count(e => e.Status == FundingEventStatuses.Requested)
                });
            }
            return Results.Ok(result);
        });

        group.MapPost("/", async (
            SaveFundingSourceRequest request, HttpContext context,
            IUserStore users, IFundingService funding, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.ConnectorConfigure, ct);
            if (error is not null) return error;

            try
            {
                var source = await funding.CreateSourceAsync(new FundingSource
                {
                    TenantId = tenantId,
                    SourceId = request.SourceId,
                    UnitId = request.UnitId?.Trim() ?? string.Empty,
                    Kind = request.Kind,
                    Name = request.Name,
                    Institution = request.Institution,
                    CommitmentAmount = request.CommitmentAmount,
                    Notes = request.Notes
                }, user!.Identifier, ct);
                return Results.Created($"/api/funding-sources/{source.SourceId}", source);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapGet("/{sourceId}/events", async (
            string sourceId, HttpContext context, IUserStore users,
            IFundingSourceStore sources, IFundingEventStore events, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            var source = await sources.GetAsync(tenantId, sourceId, ct);
            if (source is null) return Results.NotFound();
            return Results.Ok(await events.GetBySourceAsync(tenantId, source.Id, ct));
        });

        // A draw request / capital call is data entry; the DECISION is not.
        group.MapPost("/{sourceId}/events", async (
            string sourceId, RequestFundingEventRequest request, HttpContext context,
            IUserStore users, IFundingService funding, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordSubmit, ct);
            if (error is not null) return error;

            try
            {
                var fundingEvent = await funding.RequestEventAsync(tenantId, sourceId, new FundingEvent
                {
                    EventType = request.EventType,
                    Amount = request.Amount,
                    MilestoneReference = request.MilestoneReference,
                    PercentCompleteClaimed = request.PercentCompleteClaimed,
                    DocumentationReference = request.DocumentationReference,
                    VerificationNote = request.VerificationNote
                }, user!.Identifier, ct);
                return Results.Created($"/api/funding-sources/{sourceId}/events/{fundingEvent.Id}", fundingEvent);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        app.MapPost("/api/funding-events/{id:guid}/decision", async (
            Guid id, FundingDecisionRequest request, HttpContext context,
            IUserStore users, IFundingService funding, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.DualApprove, ct);
            if (error is not null) return error;

            var action = request.Action?.Trim();
            if (action is not ("Approve" or "Reject"))
                return Results.BadRequest(new { error = "action must be Approve or Reject." });

            try
            {
                return Results.Ok(await funding.DecideAsync(
                    tenantId, id, user!, action == "Approve", request.Comment, ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        app.MapPost("/api/funding-events/{id:guid}/disbursed", async (
            Guid id, HttpContext context, IUserStore users, IFundingService funding, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordSubmit, ct);
            if (error is not null) return error;

            try
            {
                return Results.Ok(await funding.MarkDisbursedAsync(tenantId, id, user!.Identifier, ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        return app;
    }
}
