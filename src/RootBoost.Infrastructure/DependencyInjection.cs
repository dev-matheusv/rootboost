using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Traffic;
using RootBoost.Application.UseCases;
using RootBoost.Infrastructure.Catalog;
using RootBoost.Infrastructure.Notifications;
using RootBoost.Infrastructure.Payments;
using RootBoost.Infrastructure.Persistence;
using RootBoost.Infrastructure.Suppliers;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Wires every adapter. Behavior is config-driven so dev can run fully mocked
    /// (Supplier=Mock, Notifier=Logging, Payments:Verifier=Test) and prod flips to the real ones.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, string contentRoot)
    {
        // --- Catalog (singleton, loaded once from catalog.json) ---
        var catalogPath = config["Catalog:Path"] ?? Path.Combine(contentRoot, "catalog.json");
        services.AddSingleton<IProductCatalog>(_ => JsonCatalog.LoadFromFile(catalogPath));

        // --- Persistence (EF Core + SQLite) ---
        var conn = config.GetConnectionString("Sqlite") ?? "Data Source=rootboost.db";
        services.AddDbContext<RootBoostDbContext>(o => o.UseSqlite(conn));
        services.AddScoped<IOrderRepository, OrderRepository>();

        // Analytics de topo de funil: contador de visitas (denominador da conversão) + relatório.
        services.AddScoped<IVisitStore, RootBoost.Infrastructure.Analytics.EfVisitStore>();
        services.AddScoped<ProductPerformanceReport>();

        // --- Options ---
        services.Configure<CjOptions>(config.GetSection(CjOptions.Section));
        services.Configure<PayPalOptions>(config.GetSection(PayPalOptions.Section));
        services.Configure<ResendOptions>(config.GetSection(ResendOptions.Section));

        // --- HTTP clients with standard resilience (retry/timeout on transient failures) ---
        var cjBase = config[$"{CjOptions.Section}:BaseUrl"] ?? "https://developers.cjdropshipping.com/api2.0/v1/";
        services.AddHttpClient("cj", c => c.BaseAddress = new Uri(cjBase)).AddStandardResilienceHandler();

        var ppBase = config[$"{PayPalOptions.Section}:BaseUrl"] ?? "https://api-m.paypal.com";
        services.AddHttpClient("paypal", c => c.BaseAddress = new Uri(ppBase.TrimEnd('/') + "/")).AddStandardResilienceHandler();

        services.AddHttpClient("resend", c =>
        {
            c.BaseAddress = new Uri("https://api.resend.com/");
            var key = config[$"{ResendOptions.Section}:ApiKey"];
            if (!string.IsNullOrWhiteSpace(key))
                c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        }).AddStandardResilienceHandler();

        // --- Supplier: Mock (dev) or Cj (prod) ---
        if (string.Equals(config["Supplier"], "Mock", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ISupplierClient, MockSupplierClient>();
        else
            services.AddSingleton<ISupplierClient, CjClient>();

        // --- Notifier: Logging (dev) or Resend (prod) ---
        if (string.Equals(config["Notifier"], "Logging", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<INotifier, LoggingNotifier>();
        else
            services.AddSingleton<INotifier, ResendNotifier>();

        // --- Payment verification + server-side checkout ---
        services.AddSingleton<PayPalClient>();
        services.AddSingleton<ICheckoutGateway, PayPalCheckoutGateway>();

        // --- Conversion tracking (Meta CAPI). Null por padrão; "Meta" liga quando configurado. ---
        services.Configure<MetaOptions>(config.GetSection(MetaOptions.Section));
        services.AddHttpClient("meta", c => c.BaseAddress = new Uri("https://graph.facebook.com/"))
            .AddStandardResilienceHandler();
        if (string.Equals(config["Tracker"], "Meta", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IConversionTracker, MetaConversionTracker>();
        else
            services.AddSingleton<IConversionTracker, NullConversionTracker>();

        // --- Automação de tráfego (o "cérebro"): otimizador + seletor de criativos + piloto. ---
        // Política ajustável via seção "Traffic". Fonte/actuator reais (Meta Ads) são TODO;
        // por padrão roda em DRY-RUN (só recomenda), sem tocar em conta de anúncios.
        var policy = new OptimizationPolicy();
        config.GetSection(OptimizationPolicy.Section).Bind(policy);
        services.AddSingleton(policy);
        services.AddSingleton<CampaignOptimizer>();
        services.AddSingleton<CreativeSelector>();
        services.AddSingleton<GrowthPlanner>();
        services.AddSingleton<ICampaignInsightsSource, NotConfiguredInsightsSource>();
        services.AddSingleton<ICampaignActuator, LoggingCampaignActuator>();
        services.AddScoped<TrafficAutopilot>();
        services.AddSingleton<PayPalWebhookVerifier>();
        services.AddSingleton<TestPaymentVerifier>();
        if (string.Equals(config["Payments:Verifier"], "Test", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IPaymentVerifier>(sp => sp.GetRequiredService<TestPaymentVerifier>());
        else
            services.AddSingleton<IPaymentVerifier>(sp => sp.GetRequiredService<PayPalWebhookVerifier>());

        // --- Use cases ---
        services.AddScoped<PlaceOrderOnPayment>();
        services.AddScoped<AttachTracking>();

        return services;
    }
}
