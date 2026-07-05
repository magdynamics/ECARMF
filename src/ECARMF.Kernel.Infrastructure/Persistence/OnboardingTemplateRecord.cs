using System.Text.Json;
using System.Text.Json.Serialization;
using ECARMF.Kernel.Application.Onboarding;
using Microsoft.EntityFrameworkCore;

namespace ECARMF.Kernel.Infrastructure.Persistence;

public class OnboardingTemplateRecord
{
    public Guid Id { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public string? Description { get; set; }
    public string CreatedFromTenant { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The full template body (packages, benchmarks, renewals) as
    /// JSON — templates are documents, not relational data.</summary>
    public string ContentJson { get; set; } = string.Empty;
}

public class EfOnboardingTemplateStore : IOnboardingTemplateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ECARMFDbContext _db;

    public EfOnboardingTemplateStore(ECARMFDbContext db) => _db = db;

    public async Task<IReadOnlyList<OnboardingTemplate>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await _db.OnboardingTemplates.AsNoTracking()
            .OrderBy(t => t.Name).ToListAsync(ct);
        return records.Select(ToDomain).Where(t => t is not null).Select(t => t!).ToList();
    }

    public async Task<OnboardingTemplate?> GetAsync(string templateId, CancellationToken ct = default)
    {
        var record = await _db.OnboardingTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TemplateId == templateId, ct);
        return record is null ? null : ToDomain(record);
    }

    public async Task UpsertAsync(OnboardingTemplate template, CancellationToken ct = default)
    {
        var record = await _db.OnboardingTemplates
            .FirstOrDefaultAsync(t => t.TemplateId == template.TemplateId, ct);
        if (record is null)
        {
            record = new OnboardingTemplateRecord { Id = template.Id, TemplateId = template.TemplateId };
            _db.OnboardingTemplates.Add(record);
        }

        record.Name = template.Name;
        record.Industry = template.Industry;
        record.Description = template.Description;
        record.CreatedFromTenant = template.CreatedFromTenant;
        record.CreatedBy = template.CreatedBy;
        record.CreatedAt = template.CreatedAt;
        record.ContentJson = JsonSerializer.Serialize(template, JsonOptions);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string templateId, CancellationToken ct = default)
    {
        var record = await _db.OnboardingTemplates
            .FirstOrDefaultAsync(t => t.TemplateId == templateId, ct);
        if (record is not null)
        {
            _db.OnboardingTemplates.Remove(record);
            await _db.SaveChangesAsync(ct);
        }
    }

    private static OnboardingTemplate? ToDomain(OnboardingTemplateRecord record)
    {
        try
        {
            return JsonSerializer.Deserialize<OnboardingTemplate>(record.ContentJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null; // a corrupt row must not take down the template list
        }
    }
}
