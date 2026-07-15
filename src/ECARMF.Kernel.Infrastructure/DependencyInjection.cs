using ECARMF.Kernel.Application.Audit;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Scoring;
using ECARMF.Kernel.Application.Transactions;
using ECARMF.Kernel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ECARMF.Kernel.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddECARMFInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        // Connection resiliency: a cold or briefly-unreachable SQL instance
        // (e.g. SQL Browser still starting after a reboot) must not crash the
        // app. Retry transient faults instead of failing the first query —
        // this is what turns a boot-time DB blip into a short delay rather
        // than a service that won't start.
        services.AddDbContext<ECARMFDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.EnableRetryOnFailure(
                    maxRetryCount: 6,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null)));

        services.AddScoped<IPackageStore, EfPackageStore>();
        services.AddScoped<Application.Packages.ISkillSettingStore, EfSkillSettingStore>();
        services.AddScoped<Application.Cases.ICaseStore, EfCaseStore>();
        services.AddScoped<Application.Risk.IRiskTreatmentStore, EfRiskTreatmentStore>();
        services.AddScoped<ITransactionStore, EfTransactionStore>();
        services.AddScoped<IOutcomeStore, EfOutcomeStore>();
        services.AddScoped<IAuditLog, EfAuditLog>();
        services.AddScoped<IApprovalStore, EfApprovalStore>();
        services.AddScoped<IScoreStore, EfScoreStore>();
        services.AddScoped<Application.Identity.IUserStore, EfUserStore>();
        services.AddScoped<Application.Ingestion.IConnectorStore, EfConnectorStore>();
        services.AddScoped<Application.Capital.ICapitalFlowStore, EfCapitalFlowStore>();
        services.AddScoped<Application.Analytics.IDeviationStore, EfDeviationStore>();
        services.AddScoped<IDashboardStore, EfDashboardStore>();
        services.AddScoped<Application.Workflow.ITaskStore, EfTaskStore>();
        services.AddScoped<Application.Workflow.INotificationStore, EfNotificationStore>();
        services.AddScoped<Application.Advisor.IAdvisorStore, EfAdvisorStore>();
        services.AddScoped<Application.Identity.ITenantDirectory, EfTenantDirectory>();
        services.AddScoped<Application.Advisor.ITenantAiSettingsStore, EfTenantAiSettingsStore>();
        services.AddScoped<Application.Advisor.ILanguageModelProvider, Ai.TenantLanguageModelProvider>();
        services.AddSingleton<Application.Ingestion.IDocumentTextReader, Ai.DocumentTextReader>();
        services.AddScoped<Application.Library.IDocumentLibrary, EfDocumentLibrary>();
        services.AddScoped<Application.Integrations.IIntegrationStore, EfIntegrationStore>();
        services.AddScoped<Application.Integrations.IFeedPuller, Ai.HttpFeedPuller>();
        services.AddScoped<Application.Analytics.IBenchmarkStore, EfBenchmarkStore>();
        services.AddScoped<Application.Compliance.IRenewalStore, EfRenewalStore>();
        services.AddScoped<Application.Notifications.INotificationOutbox, EfNotificationOutbox>();
        services.AddScoped<Application.Notifications.IMailSettingsStore, EfMailSettingsStore>();
        services.AddScoped<Application.Notifications.IEmailSender, Mail.SmtpEmailSender>();
        services.AddScoped<Application.Onboarding.IOnboardingTemplateStore, EfOnboardingTemplateStore>();
        services.AddScoped<Application.Identity.IOrgUnitStore, EfOrgUnitStore>();
        services.AddScoped<Application.Treasury.ISweepAccountStore, EfSweepAccountStore>();
        services.AddScoped<Application.Capital.IFundingSourceStore, EfFundingSourceStore>();
        services.AddScoped<Application.Capital.IFundingEventStore, EfFundingEventStore>();
        services.AddScoped<Application.Tenancy.IITAssetStore, EfITAssetStore>();
        services.AddScoped<Application.Identity.IInvestorProfileStore, EfInvestorProfileStore>();
        services.AddScoped<Application.Analysis.IFinancialStatementStore, EfFinancialStatementStore>();
        services.AddScoped<Application.Relationships.IEntityRelationshipStore, EfEntityRelationshipStore>();
        services.AddScoped<Application.Operations.IPlatformJanitor, PlatformJanitor>();
        services.AddScoped<Application.Billing.IBillingPlanStore, EfBillingPlanStore>();
        services.AddScoped<Application.Billing.IBillingStatementStore, EfBillingStatementStore>();
        services.AddScoped<Application.Billing.IUsageMeter, EfUsageMeter>();
        services.AddScoped<Application.Agents.IAgentInteractionStore, EfAgentInteractionStore>();
        services.AddHttpClient("integration-feeds", client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddDataProtection();

        return services;
    }
}
