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
        services.AddDbContext<ECARMFDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IPackageStore, EfPackageStore>();
        services.AddScoped<ITransactionStore, EfTransactionStore>();
        services.AddScoped<IOutcomeStore, EfOutcomeStore>();
        services.AddScoped<IAuditLog, EfAuditLog>();
        services.AddScoped<IApprovalStore, EfApprovalStore>();
        services.AddScoped<IScoreStore, EfScoreStore>();
        services.AddScoped<Application.Identity.IUserStore, EfUserStore>();
        services.AddScoped<Application.Ingestion.IConnectorStore, EfConnectorStore>();
        services.AddScoped<Application.Capital.IAllocationStore, EfAllocationStore>();
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
        services.AddScoped<Application.Billing.IBillingPlanStore, EfBillingPlanStore>();
        services.AddScoped<Application.Billing.IBillingStatementStore, EfBillingStatementStore>();
        services.AddScoped<Application.Billing.IUsageMeter, EfUsageMeter>();
        services.AddScoped<Application.Agents.IAgentInteractionStore, EfAgentInteractionStore>();
        services.AddHttpClient("integration-feeds", client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddDataProtection();

        return services;
    }
}
