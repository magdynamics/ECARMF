using System.Text.Json;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Transactions;

namespace ECARMF.Kernel.Api.Endpoints;

/// <summary>
/// Generic record intake and activity. Transaction and Opportunity are both
/// just registered entity types flowing through this one pipeline — there
/// are no type-specific endpoints.
/// </summary>
public static class RecordEndpoints
{
    public record SubmitRecordRequest(
        string RecordType,
        string SubmittedBy,
        Dictionary<string, JsonElement>? Payload);

    public record ApprovalRequestBody(
        string Approver,
        string Verdict,
        string? Comment);

    public static IEndpointRouteBuilder MapRecordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/records");

        group.MapPost("/", async (
            SubmitRecordRequest request,
            HttpContext context,
            ITransactionIntakeService intake,
            IUserStore users,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var (error, user) = await AccessGuard.RequireAsync(
                context, users, tenantId, Permissions.RecordSubmit, ct);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.RecordType))
                return Results.BadRequest(new { error = "recordType is required." });

            var payload = (request.Payload ?? []).ToDictionary(
                kv => kv.Key,
                kv => JsonValueToString(kv.Value));

            // The submitter is the authenticated identity, not a free-text
            // claim — segregation of duties depends on this being real.
            var receipt = await intake.ReceiveAsync(
                new TransactionSubmission(tenantId, request.RecordType, user!.Identifier, payload), ct);

            return Results.Accepted($"/api/records/{receipt.TransactionId}", receipt);
        });

        // Recent record activity with outcomes — feeds the admin UI.
        group.MapGet("/", async (
            int? limit,
            HttpContext context,
            ITransactionStore records,
            IOutcomeStore outcomes,
            IUserStore users,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var (error, _) = await AccessGuard.RequireAsync(
                context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            var take = Math.Clamp(limit ?? 50, 1, 200);
            var recent = await records.GetRecentAsync(tenantId, take, ct);
            var ids = recent.Select(t => t.TransactionId).ToList();
            var outcomesById = (await outcomes.GetForTransactionsAsync(tenantId, ids, ct))
                .GroupBy(o => o.TransactionId)
                .ToDictionary(g => g.Key, g => g.OrderBy(o => o.ProcessedAt).ToList());

            var feed = recent.Select(t => new
            {
                RecordId = t.TransactionId,
                RecordType = t.TransactionType,
                t.SubmittedBy,
                t.ReceivedAt,
                t.Payload,
                Outcomes = outcomesById.TryGetValue(t.TransactionId, out var list)
                    ? list.Select(o => new
                    {
                        o.Outcome,
                        o.Reason,
                        o.RuleId,
                        o.PackageId,
                        o.PackageVersion,
                        o.EventName,
                        o.ProcessedAt
                    })
                    : []
            });

            return Results.Ok(feed);
        });

        // Metadata query built for thousands of records: filter by type,
        // outcome, submitter, time range, and free text — with paging.
        group.MapGet("/search", async (
            string? recordType, string? outcome, string? submittedBy, string? search,
            DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize,
            HttpContext context,
            ITransactionStore records,
            IOutcomeStore outcomes,
            IUserStore users,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var (error, _) = await AccessGuard.RequireAsync(
                context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            var take = Math.Clamp(pageSize ?? 25, 1, 200);
            var currentPage = Math.Max(1, page ?? 1);

            var (items, total) = await records.QueryAsync(new TransactionQuery(
                tenantId, recordType, outcome, submittedBy, search, from, to,
                (currentPage - 1) * take, take), ct);

            var ids = items.Select(t => t.TransactionId).ToList();
            var outcomesById = (await outcomes.GetForTransactionsAsync(tenantId, ids, ct))
                .GroupBy(o => o.TransactionId)
                .ToDictionary(g => g.Key, g => g.OrderBy(o => o.ProcessedAt).ToList());

            return Results.Ok(new
            {
                total,
                page = currentPage,
                pageSize = take,
                items = items.Select(t => new
                {
                    RecordId = t.TransactionId,
                    RecordType = t.TransactionType,
                    t.SubmittedBy,
                    t.ReceivedAt,
                    t.Payload,
                    Outcomes = outcomesById.TryGetValue(t.TransactionId, out var list)
                        ? list.Select(o => new
                        {
                            o.Outcome, o.Reason, o.RuleId, o.PackageId, o.PackageVersion, o.EventName, o.ProcessedAt
                        })
                        : []
                })
            });
        });

        // Distinct record types — drives the metadata filter dropdowns.
        group.MapGet("/types", async (
            HttpContext context, ITransactionStore records, IUserStore users, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;

            return Results.Ok(await records.GetRecordTypesAsync(tenantId, ct));
        });

        // Dual approval: a second approver releases or rejects a flagged record.
        group.MapPost("/{recordId:guid}/approvals", async (
            Guid recordId,
            ApprovalRequestBody body,
            HttpContext context,
            IApprovalService approvals,
            IUserStore users,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            // Capability-scoped permission: the same enforcement point every
            // capability uses. AI system actors do not hold this permission,
            // so an AI can never self-approve an escalated/flagged outcome.
            var (error, user) = await AccessGuard.RequireAsync(
                context, users, tenantId, Permissions.DualApprove, ct);
            if (error is not null) return error;

            if (!Enum.TryParse<ApprovalVerdict>(body.Verdict, ignoreCase: true, out var verdict))
                return Results.BadRequest(new { error = "verdict must be 'Approve' or 'Reject'." });

            // The approver is the authenticated identity; ApprovalService
            // enforces segregation of duties (approver != submitter).
            var result = await approvals.DecideAsync(
                new ApprovalSubmission(tenantId, recordId, user!.Identifier, verdict, body.Comment), ct);

            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        return app;
    }

    /// <summary>Payload values arrive as arbitrary JSON; rules evaluate string
    /// representations and coerce by declared type.</summary>
    private static string JsonValueToString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => element.GetRawText()
    };
}
