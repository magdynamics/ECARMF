using ECARMF.Kernel.Application.Events;
using ECARMF.Kernel.Application.Packages;
using ECARMF.Kernel.Application.Processing;
using ECARMF.Kernel.Application.Registries;
using ECARMF.Kernel.Application.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace ECARMF.Kernel.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddECARMFApplication(this IServiceCollection services)
    {
        services.AddSingleton<ITenantRegistryProvider, TenantRegistryProvider>();
        services.AddSingleton<IKernelEventBus, InProcessKernelEventBus>();
        services.AddScoped<IPackageLoader, PackageLoader>();
        services.AddScoped<Packages.IPackageCatalog, Packages.PackageCatalogService>();
        services.AddScoped<Packages.ISkillCatalog, Packages.SkillCatalogService>();
        services.AddScoped<Packages.IPackageIdLedgerService, Packages.PackageIdLedgerService>();
        services.AddScoped<ITransactionIntakeService, TransactionIntakeService>();
        services.AddScoped<IEventProcessor, EventProcessor>();
        services.AddScoped<IApprovalService, ApprovalService>();
        services.AddScoped<Ingestion.IDataSourceConnector, Ingestion.ConnectorIngestionService>();
        services.AddScoped<Flywheel.IAILearningFeedbackService, Flywheel.AILearningFeedbackService>();
        services.AddScoped<Capital.ICapitalAllocationEngine, Capital.CapitalAllocationEngine>();
        services.AddScoped<Performance.PerformanceEvaluationService>();
        services.AddScoped<Performance.IPerformanceEvaluator>(sp => sp.GetRequiredService<Performance.PerformanceEvaluationService>());
        services.AddScoped<Performance.IFrameworkRecommender>(sp => sp.GetRequiredService<Performance.PerformanceEvaluationService>());
        services.AddScoped<Analytics.IForecastingEngine, Analytics.ForecastingEngine>();
        services.AddScoped<Analytics.ICompositeHealthService, Analytics.CompositeHealthService>();
        services.AddScoped<Analytics.IDeviationMonitor, Analytics.DeviationMonitoringService>();
        services.AddScoped<Workflow.IWorkflowEngine, Workflow.WorkflowEngine>();
        services.AddScoped<Advisor.IExecutiveAdvisor, Advisor.ExecutiveAdvisorService>();
        services.AddScoped<Ingestion.IDocumentExtractor, Ingestion.DocumentExtractionService>();
        services.AddScoped<Integrations.IIntegrationFeedService, Integrations.IntegrationFeedService>();
        services.AddScoped<Analytics.IBenchmarkMonitor, Analytics.BenchmarkMonitorService>();
        services.AddScoped<Compliance.IRenewalMonitor, Compliance.RenewalMonitorService>();
        services.AddScoped<Notifications.NotificationEmailService>();
        services.AddScoped<Reporting.IClientReportService, Reporting.ClientReportService>();
        services.AddScoped<Ingestion.IBulkImportService, Ingestion.BulkImportService>();
        services.AddScoped<Onboarding.IOnboardingTemplateService, Onboarding.OnboardingTemplateService>();
        services.AddScoped<Onboarding.IDemoSeedingService, Onboarding.DemoSeedingService>();
        services.AddScoped<Onboarding.IOnboardingAdvisor, Onboarding.OnboardingAdvisorService>();
        services.AddScoped<Analytics.IPeriodAnalysisService, Analytics.PeriodAnalysisService>();
        services.AddScoped<Cases.ICaseAnalysisService, Cases.CaseAnalysisService>();
        services.AddScoped<Analytics.IPlatformRiskService, Analytics.PlatformRiskService>();
        services.AddScoped<Analytics.IPlatformActionService, Analytics.PlatformActionService>();
        services.AddScoped<Operations.IPlatformHealthService, Operations.PlatformHealthService>();
        services.AddScoped<Billing.IMonthlyBillingService, Billing.MonthlyBillingService>();
        services.AddScoped<Analytics.IPeerBenchmarkService, Analytics.PeerBenchmarkService>();
        services.AddScoped<Identity.IOrgUnitService, Identity.OrgUnitService>();
        services.AddScoped<Treasury.ITreasurySweepService, Treasury.TreasurySweepService>();
        services.AddScoped<Capital.IFundingService, Capital.FundingService>();
        services.AddScoped<Analysis.IFinancialStatementService, Analysis.FinancialStatementService>();
        services.AddScoped<Billing.IBillingService, Billing.BillingService>();
        services.AddScoped<Agents.IAgentConsultService, Agents.AgentConsultService>();

        return services;
    }
}
