using System.Text.Json;
using ECARMF.Kernel.Application.Transactions;
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
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            if (string.IsNullOrWhiteSpace(request.RecordType))
                return Results.BadRequest(new { error = "recordType is required." });
            if (string.IsNullOrWhiteSpace(request.SubmittedBy))
                return Results.BadRequest(new { error = "submittedBy is required." });

            var payload = (request.Payload ?? []).ToDictionary(
                kv => kv.Key,
                kv => JsonValueToString(kv.Value));

            var receipt = await intake.ReceiveAsync(
                new TransactionSubmission(tenantId, request.RecordType, request.SubmittedBy, payload), ct);

            return Results.Accepted($"/api/records/{receipt.TransactionId}", receipt);
        });

        // Recent record activity with outcomes — feeds the admin UI.
        group.MapGet("/", async (
            int? limit,
            HttpContext context,
            ITransactionStore records,
            IOutcomeStore outcomes,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

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

        // Dual approval: a second approver releases or rejects a flagged record.
        group.MapPost("/{recordId:guid}/approvals", async (
            Guid recordId,
            ApprovalRequestBody body,
            HttpContext context,
            IApprovalService approvals,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            if (!Enum.TryParse<ApprovalVerdict>(body.Verdict, ignoreCase: true, out var verdict))
                return Results.BadRequest(new { error = "verdict must be 'Approve' or 'Reject'." });

            var result = await approvals.DecideAsync(
                new ApprovalSubmission(tenantId, recordId, body.Approver, verdict, body.Comment), ct);

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
