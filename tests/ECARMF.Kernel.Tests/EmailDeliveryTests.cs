using ECARMF.Kernel.Application.Notifications;
using ECARMF.Kernel.Domain.Audit;
using ECARMF.Kernel.Domain.Identity;
using ECARMF.Kernel.Domain.Tenancy;
using ECARMF.Kernel.Domain.Workflow;
using ECARMF.Kernel.Tests.Fakes;

namespace ECARMF.Kernel.Tests;

public class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];
    public bool Fail { get; set; }

    public Task SendAsync(MailDeliverySettings settings, EmailMessage message, CancellationToken ct = default)
    {
        if (Fail) throw new InvalidOperationException("SMTP unreachable");
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

public class FakeMailSettingsStore : IMailSettingsStore
{
    public MailDeliverySettings? Settings { get; set; }

    public Task<MailDeliverySettings?> GetAsync(CancellationToken ct = default) => Task.FromResult(Settings);

    public Task<MailSettingsStatus> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new MailSettingsStatus(
            Settings is not null, Settings?.Enabled ?? false, Settings?.Host, Settings?.Port,
            Settings?.UseSsl, Settings?.Username, Settings?.FromAddress, Settings?.MinSeverity, null, null));

    public Task SetAsync(MailDeliverySettings settings, string configuredBy, CancellationToken ct = default)
    {
        Settings = settings;
        return Task.CompletedTask;
    }
}

public class InMemoryNotificationOutbox : INotificationOutbox
{
    public List<NotificationItem> Pending { get; } = [];
    public Dictionary<Guid, string> Outcomes { get; } = [];

    public Task<IReadOnlyList<NotificationItem>> GetPendingAsync(int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NotificationItem>>(
            Pending.Where(n => !Outcomes.ContainsKey(n.Id)).Take(limit).ToList());

    public Task MarkProcessedAsync(Guid notificationId, string outcome, CancellationToken ct = default)
    {
        Outcomes[notificationId] = outcome;
        return Task.CompletedTask;
    }
}

public class FakeTenantDirectory : ECARMF.Kernel.Application.Identity.ITenantDirectory
{
    public Dictionary<string, TenantProfile> Profiles { get; } = [];

    public Task<TenantProfile?> GetAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult(Profiles.TryGetValue(tenantId, out var p) ? p : null);

    public Task<IReadOnlyList<TenantProfile>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TenantProfile>>(Profiles.Values.ToList());

    public Task AddAsync(TenantProfile profile, CancellationToken ct = default)
    {
        Profiles[profile.TenantId] = profile;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(TenantProfile profile, CancellationToken ct = default)
    {
        Profiles[profile.TenantId] = profile;
        return Task.CompletedTask;
    }
}

/// <summary>Alarms leave the building: severity-filtered email dispatch to
/// role holders, contact fallback, once-only processing, failures visible.</summary>
public class EmailDeliveryTests
{
    private const string Tenant = "tenant-a";

    private readonly InMemoryNotificationOutbox _outbox = new();
    private readonly FakeMailSettingsStore _settings = new();
    private readonly FakeEmailSender _sender = new();
    private readonly InMemoryUserStore _users = new();
    private readonly FakeTenantDirectory _tenants = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly NotificationEmailService _service;

    public EmailDeliveryTests()
    {
        _service = new NotificationEmailService(_outbox, _settings, _sender, _users, _tenants, _audit);
        _settings.Settings = new MailDeliverySettings(
            true, "mail.local", 25, false, null, null, "alerts@ecarmf.local", "Warning");
    }

    private NotificationItem Queue(string severity, string target = "ExecutiveOwner")
    {
        var notification = new NotificationItem
        {
            TenantId = Tenant,
            WorkflowId = "benchmark:x",
            Target = target,
            Message = $"{severity} alarm for testing",
            Severity = severity,
            CorrelationId = Guid.NewGuid()
        };
        _outbox.Pending.Add(notification);
        return notification;
    }

    private void AddUser(string identifier, string role, string? email, string status = "Active")
    {
        _users.Items.Add(new User
        {
            TenantId = Tenant,
            Identifier = identifier,
            DisplayName = identifier,
            Roles = [role],
            Email = email,
            Status = status
        });
    }

    [Fact]
    public async Task Unconfigured_mail_leaves_queue_untouched()
    {
        _settings.Settings = null;
        Queue("Critical");

        Assert.Equal(0, await _service.ProcessPendingAsync());
        Assert.Empty(_outbox.Outcomes); // still pending: delivered once configured
    }

    [Fact]
    public async Task Sends_to_role_holders_and_marks_processed_once()
    {
        AddUser("owner@a", "ExecutiveOwner", "owner@client-a.com");
        AddUser("cfo@a", "ExecutiveOwner", "cfo@client-a.com");
        AddUser("clerk@a", "Clerk", "clerk@client-a.com"); // different role: not a recipient
        var notification = Queue("Critical");

        Assert.Equal(1, await _service.ProcessPendingAsync());
        var sent = Assert.Single(_sender.Sent);
        Assert.Equal(2, sent.To.Count);
        Assert.Contains("owner@client-a.com", sent.To);
        Assert.DoesNotContain("clerk@client-a.com", sent.To);
        Assert.StartsWith("sent:", _outbox.Outcomes[notification.Id]);
        Assert.Contains(_audit.Items, a => a.Category == AuditCategories.NotificationEmailed);

        // Second pass: nothing left.
        Assert.Equal(0, await _service.ProcessPendingAsync());
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task Below_threshold_severity_is_skipped_not_sent()
    {
        AddUser("owner@a", "ExecutiveOwner", "owner@client-a.com");
        var info = Queue("Info");

        Assert.Equal(0, await _service.ProcessPendingAsync());
        Assert.Empty(_sender.Sent);
        Assert.StartsWith("skipped:", _outbox.Outcomes[info.Id]);
    }

    [Fact]
    public async Task Falls_back_to_tenant_contact_when_no_role_holder_has_email()
    {
        AddUser("owner@a", "ExecutiveOwner", email: null);
        _tenants.Profiles[Tenant] = new TenantProfile
        {
            TenantId = Tenant, Name = "Client A", ContactEmail = "contact@client-a.com"
        };
        Queue("Critical");

        Assert.Equal(1, await _service.ProcessPendingAsync());
        Assert.Equal("contact@client-a.com", Assert.Single(Assert.Single(_sender.Sent).To));
    }

    [Fact]
    public async Task Send_failure_is_recorded_and_does_not_block_the_queue()
    {
        AddUser("owner@a", "ExecutiveOwner", "owner@client-a.com");
        _sender.Fail = true;
        var notification = Queue("Critical");

        Assert.Equal(0, await _service.ProcessPendingAsync());
        Assert.StartsWith("failed:", _outbox.Outcomes[notification.Id]);
        Assert.Contains("SMTP unreachable", _outbox.Outcomes[notification.Id]);
    }

    [Fact]
    public async Task Disabled_users_and_system_actors_never_receive_mail()
    {
        AddUser("gone@a", "ExecutiveOwner", "gone@client-a.com", status: "Disabled");
        _users.Items.Add(new User
        {
            TenantId = Tenant, Identifier = "system:flywheel", DisplayName = "AI",
            Roles = ["ExecutiveOwner"], Email = "ai@internal", IsSystemActor = true, Status = "Active"
        });
        var notification = Queue("Critical");

        Assert.Equal(0, await _service.ProcessPendingAsync());
        Assert.Empty(_sender.Sent);
        Assert.StartsWith("skipped: no email", _outbox.Outcomes[notification.Id]);
    }
}
