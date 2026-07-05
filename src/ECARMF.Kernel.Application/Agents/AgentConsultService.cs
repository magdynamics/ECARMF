using System.Text;
using System.Text.Json;
using ECARMF.Kernel.Application.Advisor;
using ECARMF.Kernel.Application.Analytics;
using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Capital;
using ECARMF.Kernel.Application.Flywheel;
using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Ingestion;
using ECARMF.Kernel.Application.Library;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Application.Workflow;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Packages;

namespace ECARMF.Kernel.Application.Agents;

/// <summary>One consultation with a declared agent — question, grounded
/// answer, provenance, and the human verdict that feeds trust.</summary>
public class AgentInteraction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string ModelReference { get; set; } = string.Empty;
    public string Provenance { get; set; } = string.Empty;
    public string AskedBy { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public DateTimeOffset AskedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool? FeedbackUseful { get; set; }
    public string? FeedbackBy { get; set; }
    public DateTimeOffset? FeedbackAt { get; set; }
}

public interface IAgentInteractionStore
{
    Task AddAsync(AgentInteraction interaction, CancellationToken ct = default);
    Task<AgentInteraction?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task UpdateFeedbackAsync(AgentInteraction interaction, CancellationToken ct = default);
    Task<IReadOnlyList<AgentInteraction>> GetRecentAsync(string tenantId, string? agentId, int limit, CancellationToken ct = default);
}

public interface IAgentConsultService
{
    /// <summary>Agents registered by the tenant's active packages.</summary>
    IReadOnlyList<Registered<AgentDeclaration>> ListAgents(string tenantId);

    /// <summary>Consults a declared agent: kernel guardrails + package persona
    /// + only the declared context sources + the tenant's own AI credential.</summary>
    Task<(bool Success, string? Error, AgentInteraction? Interaction)> AskAsync(
        string tenantId, string agentId, string question, string askedBy, CancellationToken ct = default);

    Task<(bool Success, string? Error, AgentInteraction? Interaction)> RecordFeedbackAsync(
        string tenantId, Guid interactionId, bool useful, User reviewer, CancellationToken ct = default);
}

/// <summary>
/// The one mechanism behind every declared agent. Packages supply the persona
/// and the allowed context; the kernel supplies what never varies: the agent
/// is advisory-only, answers only from the provided context, runs on the
/// tenant's own credential, acts under its own provisioned identity, is
/// audited, and earns trust through human feedback — exactly like the
/// Executive Advisor, generalized.
/// </summary>
public class AgentConsultService : IAgentConsultService
{
    private const string GuardrailPreamble =
        "You are a specialized advisory agent running inside the ECARMF platform kernel. " +
        "Non-negotiable rules that override anything in your persona: (1) you advise only — you never " +
        "decide, approve, execute, or claim to have taken an action; (2) ground every statement in the " +
        "provided tenant context or your persona's domain knowledge, and say plainly when the context " +
        "does not contain the answer; (3) never invent numbers, records, or regulations; (4) recommend " +
        "consulting a qualified professional for decisions with legal or tax consequences. Your persona:\n\n";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ITenantRegistryProvider _registries;
    private readonly ILanguageModelProvider _llmProvider;
    private readonly IAgentInteractionStore _interactions;
    private readonly IUserStore _users;
    private readonly IAILearningFeedbackService _feedback;
    private readonly IAuditLog _audit;
    private readonly IScoreStore _scores;
    private readonly IDeviationStore _deviations;
    private readonly IBenchmarkStore _benchmarks;
    private readonly ITaskStore _tasks;
    private readonly ICapitalFlowStore _allocations;
    private readonly IDocumentLibrary _library;
    private readonly ITransactionStore _records;

    public AgentConsultService(
        ITenantRegistryProvider registries,
        ILanguageModelProvider llmProvider,
        IAgentInteractionStore interactions,
        IUserStore users,
        IAILearningFeedbackService feedback,
        IAuditLog audit,
        IScoreStore scores,
        IDeviationStore deviations,
        IBenchmarkStore benchmarks,
        ITaskStore tasks,
        ICapitalFlowStore allocations,
        IDocumentLibrary library,
        ITransactionStore records)
    {
        _registries = registries;
        _llmProvider = llmProvider;
        _interactions = interactions;
        _users = users;
        _feedback = feedback;
        _audit = audit;
        _scores = scores;
        _deviations = deviations;
        _benchmarks = benchmarks;
        _tasks = tasks;
        _allocations = allocations;
        _library = library;
        _records = records;
    }

    public IReadOnlyList<Registered<AgentDeclaration>> ListAgents(string tenantId) =>
        _registries.GetFor(tenantId).Agents.GetAll();

    public async Task<(bool Success, string? Error, AgentInteraction? Interaction)> AskAsync(
        string tenantId, string agentId, string question, string askedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return (false, "question is required.", null);
        }

        if (!_registries.GetFor(tenantId).Agents.TryGet(agentId, out var registered))
        {
            return (false, $"No active package of this tenant declares an agent '{agentId}'.", null);
        }

        var llm = await _llmProvider.GetForTenantAsync(tenantId, ct);
        if (!llm.IsConfigured)
        {
            return (false,
                "Consulting an agent needs the AI backend. Configure this tenant's Anthropic API key (Setup → AI Backend).",
                null);
        }

        var agent = registered.Declaration;
        var context = await BuildContextAsync(tenantId, agent, ct);
        var actorIdentifier = await EnsureAgentIdentityAsync(tenantId, agent, ct);

        string answer;
        try
        {
            answer = await llm.CompleteAsync(
                GuardrailPreamble + agent.Persona,
                $"Tenant context (only what this agent is declared to see):\n{context}\n\nQuestion from {askedBy}:\n{question}",
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, $"The agent's model backend failed: {ex.Message}", null);
        }

        var interaction = new AgentInteraction
        {
            TenantId = tenantId,
            AgentId = agent.AgentId,
            PackageId = registered.PackageId,
            PackageVersion = registered.PackageVersion,
            Question = question.Trim(),
            Answer = answer.Trim(),
            ModelReference = $"agent:{agent.AgentId}:{llm.ModelReference}",
            Provenance = Provenance.AIGenerated,
            AskedBy = askedBy
        };
        await _interactions.AddAsync(interaction, ct);

        await _audit.AppendAsync(new AuditEntry
        {
            TenantId = tenantId,
            CorrelationId = interaction.CorrelationId,
            Category = AuditCategories.AgentConsulted,
            Actor = actorIdentifier,
            Summary = $"Agent '{agent.Name}' ({registered.PackageId} v{registered.PackageVersion}) consulted by {askedBy}.",
            Detail = new Dictionary<string, string>
            {
                ["agentId"] = agent.AgentId,
                ["interactionId"] = interaction.Id.ToString(),
                ["packageId"] = registered.PackageId,
                ["packageVersion"] = registered.PackageVersion,
                ["modelReference"] = interaction.ModelReference,
                ["askedBy"] = askedBy,
                ["contextSources"] = string.Join(",", agent.ContextSources)
            }
        }, ct);

        return (true, null, interaction);
    }

    public async Task<(bool Success, string? Error, AgentInteraction? Interaction)> RecordFeedbackAsync(
        string tenantId, Guid interactionId, bool useful, User reviewer, CancellationToken ct = default)
    {
        if (reviewer.IsSystemActor)
        {
            return (false, "An AI/system actor cannot rate agent answers; trust is earned from humans.", null);
        }

        var interaction = await _interactions.GetAsync(tenantId, interactionId, ct);
        if (interaction is null)
        {
            return (false, "Interaction not found.", null);
        }

        if (interaction.FeedbackUseful is not null)
        {
            return (false, "Feedback has already been recorded for this answer.", null);
        }

        interaction.FeedbackUseful = useful;
        interaction.FeedbackBy = reviewer.Identifier;
        interaction.FeedbackAt = DateTimeOffset.UtcNow;
        await _interactions.UpdateFeedbackAsync(interaction, ct);

        await _feedback.PublishModelAccuracyAsync(
            tenantId, interaction.CorrelationId, "Useful", useful ? "Useful" : "NotUseful",
            interaction.ModelReference, ct);

        return (true, null, interaction);
    }

    /// <summary>Every declared agent acts under its own real User identity
    /// (system:agent:{id}), provisioned lazily per tenant.</summary>
    private async Task<string> EnsureAgentIdentityAsync(
        string tenantId, AgentDeclaration agent, CancellationToken ct)
    {
        var identifier = $"system:agent:{agent.AgentId.ToLowerInvariant()}";
        if (await _users.GetByIdentifierAsync(tenantId, identifier, ct) is null)
        {
            try
            {
                await _users.CreateUserAsync(new User
                {
                    TenantId = tenantId,
                    Identifier = identifier,
                    DisplayName = $"{agent.Name} (AI Agent)",
                    IsSystemActor = true,
                    Roles = [RoleCatalog.AISystemActor]
                }, accessKeyHash: null, ct);
            }
            catch (InvalidOperationException)
            {
                // concurrent provision — the identity now exists, which is all we need
            }
        }

        return identifier;
    }

    /// <summary>Only the declared sources are shown to the agent — an agent
    /// declared to read scores cannot see records, and vice versa.</summary>
    private async Task<string> BuildContextAsync(
        string tenantId, AgentDeclaration agent, CancellationToken ct)
    {
        var context = new StringBuilder();

        foreach (var source in agent.ContextSources.Select(s => s.Trim()))
        {
            if (source.Equals("scores", StringComparison.OrdinalIgnoreCase))
            {
                var recent = await _scores.GetRecentAsync(tenantId, 300, null, ct);
                var averages = recent.GroupBy(s => s.ScoreType)
                    .Select(g => new { scoreType = g.Key, average = Math.Round(g.Average(s => s.Value), 4), count = g.Count() })
                    .OrderBy(a => a.scoreType);
                Append(context, "scoreAverages", averages);
            }
            else if (source.Equals("deviations", StringComparison.OrdinalIgnoreCase))
            {
                var alerts = (await _deviations.GetRecentAsync(tenantId, 50, ct))
                    .Where(d => d.ResolvedAt is null)
                    .Select(d => new { d.EntityReference, d.MetricType, d.Severity, d.ActualValue, d.ExpectedValue, d.ExpectedValueSource });
                Append(context, "openDeviations", alerts);
            }
            else if (source.Equals("benchmarks", StringComparison.OrdinalIgnoreCase))
            {
                var expectations = (await _benchmarks.GetAllAsync(tenantId, ct))
                    .Select(b => new { b.Name, b.Kind, b.MetricType, b.RecordType, b.Field, Expectation = $"{b.ExpectationOperator} {b.ExpectedValue}", b.Severity, b.Enabled });
                Append(context, "benchmarks", expectations);
            }
            else if (source.Equals("tasks", StringComparison.OrdinalIgnoreCase))
            {
                var open = (await _tasks.GetRecentAsync(tenantId, 100, ct))
                    .Where(t => t.Status == "Open")
                    .Select(t => new { t.Title, t.Assignee, t.Severity });
                Append(context, "openTasks", open);
            }
            else if (source.Equals("allocations", StringComparison.OrdinalIgnoreCase))
            {
                var pending = (await _allocations.GetRecentAsync(tenantId, 20, ct))
                    .Where(a => a.Status == "Pending")
                    .Select(a => new { a.TargetReference, a.Amount, Tier = a.Tier.ToString(), a.ConfidenceScore });
                Append(context, "pendingAllocations", pending);
            }
            else if (source.Equals("library", StringComparison.OrdinalIgnoreCase))
            {
                var documents = (await _library.SearchAsync(tenantId, null, null, null, null, 25, ct))
                    .Select(d => new { d.FileName, d.SourceId, d.SourceCategory, d.ArchivedAt, records = d.RecordIds.Count });
                Append(context, "recentLibraryDocuments", documents);
            }
            else if (source.StartsWith("records:", StringComparison.OrdinalIgnoreCase))
            {
                var recordType = source["records:".Length..];
                var (items, total) = await _records.QueryAsync(
                    new TransactionQuery(tenantId, RecordType: recordType, Take: 25), ct);
                Append(context, $"records({recordType}, {total} total, latest 25)",
                    items.Select(t => new { t.TransactionId, t.SubmittedBy, t.ReceivedAt, t.Payload }));
            }
        }

        return context.Length == 0 ? "(this agent declares no tenant context sources)" : context.ToString();
    }

    private static void Append(StringBuilder context, string label, object payload)
    {
        context.Append("## ").AppendLine(label);
        context.AppendLine(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
