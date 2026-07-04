using System.Text.Json;
using ECARMF.Kernel.Application.Transactions;

namespace ECARMF.Kernel.Api.Endpoints;

public static class TransactionEndpoints
{
    public record SubmitTransactionRequest(
        string TransactionType,
        string SubmittedBy,
        Dictionary<string, JsonElement>? Payload);

    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/transactions", async (
            SubmitTransactionRequest request,
            HttpContext context,
            ITransactionIntakeService intake,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            if (string.IsNullOrWhiteSpace(request.TransactionType))
                return Results.BadRequest(new { error = "transactionType is required." });
            if (string.IsNullOrWhiteSpace(request.SubmittedBy))
                return Results.BadRequest(new { error = "submittedBy is required." });

            var payload = (request.Payload ?? []).ToDictionary(
                kv => kv.Key,
                kv => JsonValueToString(kv.Value));

            var receipt = await intake.ReceiveAsync(
                new TransactionSubmission(tenantId, request.TransactionType, request.SubmittedBy, payload), ct);

            return Results.Accepted($"/api/transactions/{receipt.TransactionId}", receipt);
        });

        // Recent transaction activity with outcomes — feeds the admin UI.
        app.MapGet("/api/transactions", async (
            int? limit,
            HttpContext context,
            ITransactionStore transactions,
            IOutcomeStore outcomes,
            CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();

            var take = Math.Clamp(limit ?? 50, 1, 200);
            var recent = await transactions.GetRecentAsync(tenantId, take, ct);
            var ids = recent.Select(t => t.TransactionId).ToList();
            var outcomesById = (await outcomes.GetForTransactionsAsync(tenantId, ids, ct))
                .GroupBy(o => o.TransactionId)
                .ToDictionary(g => g.Key, g => g.OrderBy(o => o.ProcessedAt).ToList());

            var feed = recent.Select(t => new
            {
                t.TransactionId,
                t.TransactionType,
                t.SubmittedBy,
                t.ReceivedAt,
                t.Payload,
                Outcomes = outcomesById.TryGetValue(t.TransactionId, out var list)
                    ? list.Select(o => new
                    {
                        Outcome = o.Outcome.ToString(),
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
