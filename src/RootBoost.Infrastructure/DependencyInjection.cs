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

        // --- Conhecimento geral de marketing (knowledge/), carregado e validado no boot ---
        var knowledgePath = config["Knowledge:Path"] ?? Path.Combine(contentRoot, "knowledge");
        services.AddSingleton<IKnowledgeBase>(_ => RootBoost.Infrastructure.Knowledge.FileKnowledgeBase.Load(knowledgePath));

        // --- Persistence (EF Core): Postgres sempre (ConnectionStrings__Postgres ou DATABASE_URL) ---
        services.AddDbContext<RootBoostDbContext>(o => DatabaseSetup.Configure(o, config));
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IBrandStore, EfBrandStore>();
        services.AddScoped<IBrandProfileStore, EfBrandProfileStore>();

        // --- Publicação própria (Fase 4). Social:Live=false (padrão) = só o publicador de mentira: nada sai pra rede. ---
        services.AddScoped<RootBoost.Application.Publishing.IPublicationStore, EfPublicationStore>();
        if (!string.Equals(config["Social:Live"], "true", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var channel in Enum.GetValues<RootBoost.Domain.SocialChannel>())
                services.AddSingleton<RootBoost.Application.Publishing.ISocialPublisher>(
                    new RootBoost.Infrastructure.Publishing.FakeSocialPublisher(channel));
        }
        else
        {
            // Publicação de verdade (RB-22): Instagram e Página do Facebook. LinkedIn ainda sem publicador: o job
            // marca o post com erro claro em vez de fingir que publicou.
            services.AddSingleton(new RootBoost.Infrastructure.Publishing.SocialAccounts(config));
            services.AddSingleton<RootBoost.Application.Publishing.ISocialPublisher, RootBoost.Infrastructure.Publishing.InstagramPublisher>();
            services.AddSingleton<RootBoost.Application.Publishing.ISocialPublisher, RootBoost.Infrastructure.Publishing.FacebookPagePublisher>();
        }
        services.AddScoped<RootBoost.Application.Publishing.ApprovePublication>();
        services.AddScoped<RootBoost.Application.Publishing.PublishDuePublications>();
        if (!string.Equals(config["Social:SchedulerEnabled"], "false", StringComparison.OrdinalIgnoreCase))
            services.AddHostedService<RootBoost.Infrastructure.Publishing.PublicationScheduler>();

        // --- Anúncios (RB-13): só leitura. Ads:Reader=None (padrão) não chama nada; Fake = dados de mentira. ---
        var adsReader = config["Ads:Reader"] ?? "None";
        var fakeAds = string.Equals(adsReader, "Fake", StringComparison.OrdinalIgnoreCase);
        services.AddSingleton<RootBoost.Application.Ads.IAdAccountDirectory>(new RootBoost.Infrastructure.Ads.ConfigAdAccountDirectory(config, fakeAds));
        if (fakeAds) services.AddSingleton<RootBoost.Application.Ads.IAdInsightsReader, RootBoost.Infrastructure.Ads.FakeAdInsightsReader>();
        else if (string.Equals(adsReader, "Meta", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<RootBoost.Application.Ads.IAdInsightsReader, RootBoost.Infrastructure.Ads.MetaAdsInsightsReader>();
        var stopRule = new RootBoost.Application.Ads.AdsStopRule();
        config.GetSection(RootBoost.Application.Ads.AdsStopRule.Section).Bind(stopRule);
        services.AddSingleton(stopRule);
        services.AddScoped<RootBoost.Application.Ads.IAdInsightStore, EfAdInsightStore>();
        services.AddScoped<RootBoost.Application.Ads.SyncAdInsights>();
        if (!string.Equals(adsReader, "None", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(config["Ads:SyncEnabled"], "false", StringComparison.OrdinalIgnoreCase))
            services.AddHostedService<RootBoost.Infrastructure.Ads.AdInsightsScheduler>();

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

        // Stripe: conta BR consegue COBRAR em USD e liquidar em BRL (o PayPal BR nao consegue,
        // recusa com UNSUPPORTED_PAYEE_CURRENCY). Por isso Stripe e o provedor alvo.
        services.Configure<StripeOptions>(config.GetSection(StripeOptions.Section));
        services.AddHttpClient("stripe", c =>
        {
            c.BaseAddress = new Uri("https://api.stripe.com/");
            var key = config[$"{StripeOptions.Section}:SecretKey"];
            if (!string.IsNullOrWhiteSpace(key))
                c.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        }).AddStandardResilienceHandler();

        // Provedor de checkout: Stripe (hosted) ou PayPal. Default PayPal pra nao mudar
        // comportamento de quem ja esta rodando sem setar nada.
        if (string.Equals(config["Payments:Provider"], "Stripe", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ICheckoutGateway, StripeCheckoutGateway>();
        else
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
        services.AddSingleton<StripeWebhookVerifier>();
        services.AddSingleton<TestPaymentVerifier>();
        var verifier = config["Payments:Verifier"];
        if (string.Equals(verifier, "Test", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IPaymentVerifier>(sp => sp.GetRequiredService<TestPaymentVerifier>());
        else if (string.Equals(verifier, "Stripe", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IPaymentVerifier>(sp => sp.GetRequiredService<StripeWebhookVerifier>());
        else
            services.AddSingleton<IPaymentVerifier>(sp => sp.GetRequiredService<PayPalWebhookVerifier>());

        // --- Use cases ---
        services.AddScoped<PlaceOrderOnPayment>();
        services.AddScoped<AttachTracking>();

        return services;
    }
}
