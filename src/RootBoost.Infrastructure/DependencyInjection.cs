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
        // Avisos de operação (RB-58): post que falhou, sync de anúncio, conexão da Meta. Log + tela; push depois.
        services.AddScoped<RootBoost.Application.Alerts.IAlertStore, EfAlertStore>();
        // Push da PWA (RB-37): sem Push__PublicKey, Push__PrivateKey e Push__Subject nada é enviado.
        var push = new RootBoost.Application.Push.PushOptions();
        config.GetSection("Push").Bind(push);
        services.AddSingleton(push);
        services.AddScoped<RootBoost.Application.Push.IPushDeviceStore, EfPushDeviceStore>();
        services.AddHttpClient<RootBoost.Application.Push.IPushTransport, RootBoost.Infrastructure.Push.WebPushTransport>();
        services.AddScoped<RootBoost.Application.Push.PushNotifier>();
        services.AddScoped<RootBoost.Application.Push.RemindPendingApprovals>();
        services.AddScoped<RootBoost.Application.Reports.WeeklyDigest>();
        services.AddScoped<RootBoost.Application.Alerts.AlertService>();
        // IA (decisão de 28/10: Claude agora atrás da porta ILanguageModel, Root AI depois). Ai:Mode=Off por padrão.
        var ai = new RootBoost.Application.Ai.AiOptions();
        config.GetSection("Ai").Bind(ai);
        ai.ApiKey ??= config["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        services.AddSingleton(ai);
        services.AddSingleton<RootBoost.Application.Ai.ILanguageModel>(sp => ai.Mode switch
        {
            RootBoost.Application.Ai.AiMode.Fake => new RootBoost.Infrastructure.Ai.FakeLanguageModel(),
            RootBoost.Application.Ai.AiMode.Claude when !string.IsNullOrWhiteSpace(ai.ApiKey) => new RootBoost.Infrastructure.Ai.ClaudeLanguageModel(ai,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RootBoost.Infrastructure.Ai.ClaudeLanguageModel>>()),
            _ => new RootBoost.Infrastructure.Ai.DisabledLanguageModel(),
        });
        services.AddScoped<RootBoost.Application.Ai.IAiUsageStore, EfAiUsageStore>();
        services.AddScoped<RootBoost.Application.Ai.ScriptWriter>();
        services.AddScoped<RootBoost.Application.Ai.IBrandProductStore, EfBrandProductStore>();
        services.AddSingleton<RootBoost.Application.Ai.IWebPageReader, RootBoost.Infrastructure.Ai.SafeWebPageReader>();
        services.AddScoped<RootBoost.Application.Ai.ProductInterviewer>();
        services.AddScoped<RootBoost.Application.Ai.ScreenAgent>();
        services.AddScoped<RootBoost.Application.Ai.ScreenGuideWriter>();
        services.AddScoped<RootBoost.Application.Ai.SystemMapper>();
        services.AddSingleton<RootBoost.Application.Ai.IDocumentTextExtractor, RootBoost.Infrastructure.Documents.DocumentTextExtractor>();
        services.AddScoped<RootBoost.Application.Ai.MediaDescriber>();
        services.AddScoped<RootBoost.Application.Studio.INarrationVoiceStore, EfNarrationVoiceStore>();
        services.AddScoped<RootBoost.Application.Studio.IStudioStatusStore, EfStudioStatusStore>();
        services.AddScoped<RootBoost.Application.Studio.IDemoReset, EfDemoReset>();
        services.AddSingleton<RootBoost.Application.Media.IImageConverter, RootBoost.Infrastructure.Media.SkiaImageConverter>();
        // Estúdio (RB-42): fila de jobs de criativo que o worker local puxa. Sem Studio:WorkerKey as rotas do worker ficam fechadas.
        services.AddScoped<RootBoost.Application.Studio.ICreativeJobStore, EfCreativeJobStore>();
        var studio = new RootBoost.Application.Studio.StudioOptions();
        config.GetSection("Studio").Bind(studio);
        services.AddSingleton(studio);
        if (!string.Equals(config["Social:Live"], "true", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var channel in Enum.GetValues<RootBoost.Domain.SocialChannel>())
                services.AddSingleton<RootBoost.Application.Publishing.ISocialPublisher>(
                    new RootBoost.Infrastructure.Publishing.FakeSocialPublisher(channel));
            services.AddSingleton<RootBoost.Application.Publishing.IMusicCatalog, RootBoost.Infrastructure.Publishing.FakeMusicCatalog>();
        }
        else
        {
            // Publicação de verdade (RB-22): Instagram e Página do Facebook. LinkedIn ainda sem publicador: o job
            // marca o post com erro claro em vez de fingir que publicou.
            services.AddSingleton(new RootBoost.Infrastructure.Publishing.SocialAccounts(config));
            services.AddSingleton<RootBoost.Application.Publishing.ISocialPublisher, RootBoost.Infrastructure.Publishing.InstagramPublisher>();
            services.AddSingleton<RootBoost.Application.Publishing.ISocialPublisher, RootBoost.Infrastructure.Publishing.FacebookPagePublisher>();
            services.AddSingleton<RootBoost.Application.Publishing.IMusicCatalog, RootBoost.Infrastructure.Publishing.InstagramMusicCatalog>();
            services.AddScoped<RootBoost.Infrastructure.Publishing.MetaConnectionCheck>();
            if (!string.Equals(config["Social:HealthCheckEnabled"], "false", StringComparison.OrdinalIgnoreCase))
                services.AddHostedService<RootBoost.Infrastructure.Publishing.MetaConnectionScheduler>();
        }
        services.AddScoped<RootBoost.Application.Publishing.ApprovePublication>();
        services.AddScoped<RootBoost.Application.Publishing.PublishDuePublications>();
        if (!string.Equals(config["Social:SchedulerEnabled"], "false", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHostedService<RootBoost.Infrastructure.Publishing.PublicationScheduler>();
            services.AddHostedService<RootBoost.Infrastructure.Push.ApprovalReminderScheduler>();
            services.AddHostedService<RootBoost.Infrastructure.Push.WeeklyDigestScheduler>();
        }

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
        // Campanha pela IA (RB-31): Ads:Creator=Meta cria de verdade (sempre pausada); padrão Fake, nada sai pra Meta.
        services.AddScoped<RootBoost.Application.Ads.IAdCampaignStore, EfAdCampaignStore>();
        services.AddScoped<RootBoost.Application.Ads.CampaignProposer>();
        services.AddScoped<RootBoost.Application.Reports.WeeklyAdvisor>();
        services.AddScoped<RootBoost.Application.Reports.MarketingAssistant>();
        services.AddSingleton<RootBoost.Application.Studio.LocalVoiceRegistry>();
        services.AddScoped<RootBoost.Application.Ads.CreateApprovedCampaign>();
        if (string.Equals(config["Ads:Creator"], "Meta", StringComparison.OrdinalIgnoreCase))
        {
            Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton(services,
                new RootBoost.Infrastructure.Publishing.SocialAccounts(config));
            services.AddSingleton<RootBoost.Application.Ads.IAdCampaignCreator, RootBoost.Infrastructure.Ads.MetaAdCampaignCreator>();
            services.AddSingleton<RootBoost.Application.Ads.IAdCampaignActuator, RootBoost.Infrastructure.Ads.MetaAdCampaignActuator>();
        }
        else
        {
            services.AddSingleton<RootBoost.Application.Ads.IAdCampaignCreator, RootBoost.Infrastructure.Ads.FakeAdCampaignCreator>();
            services.AddSingleton<RootBoost.Application.Ads.IAdCampaignActuator, RootBoost.Infrastructure.Ads.FakeAdCampaignActuator>();
        }
        services.AddScoped<RootBoost.Application.Ads.SyncAdInsights>();
        if (!string.Equals(adsReader, "None", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(config["Ads:SyncEnabled"], "false", StringComparison.OrdinalIgnoreCase))
            services.AddHostedService<RootBoost.Infrastructure.Ads.AdInsightsScheduler>();

        // --- Métricas orgânicas (RB-25): Organic:Reader=Meta lê o Instagram de verdade (só leitura), Fake = números
        // fixos, None (padrão) não chama nada. ---
        services.AddScoped<RootBoost.Application.Organic.IOrganicPostStore, RootBoost.Infrastructure.Organic.EfOrganicPostStore>();
        services.AddScoped<RootBoost.Application.Organic.SyncOrganicInsights>();
        var organicReader = config["Organic:Reader"] ?? "None";
        if (string.Equals(organicReader, "Fake", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<RootBoost.Application.Organic.IOrganicInsightsReader>(new RootBoost.Infrastructure.Organic.FakeOrganicInsightsReader(
                (config["Organic:FakeBrands"] ?? "medlycare").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        else if (string.Equals(organicReader, "Meta", StringComparison.OrdinalIgnoreCase))
        {
            Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton(services,
                new RootBoost.Infrastructure.Publishing.SocialAccounts(config));
            services.AddSingleton<RootBoost.Application.Organic.IOrganicInsightsReader, RootBoost.Infrastructure.Organic.InstagramInsightsReader>();
            // Facebook (13/11): lê os Reels publicados pela plataforma; depende das publicações, por isso por requisição.
            services.AddScoped<RootBoost.Application.Organic.IOrganicInsightsReader, RootBoost.Infrastructure.Organic.FacebookInsightsReader>();
        }
        if (!string.Equals(organicReader, "None", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(config["Organic:SyncEnabled"], "false", StringComparison.OrdinalIgnoreCase))
            services.AddHostedService<RootBoost.Infrastructure.Organic.OrganicInsightsScheduler>();

        // --- Caixa de entrada (Onda 5c): Inbox:Reader=Meta lê o Instagram Direct (só leitura), Fake = conversas
        // fictícias, None (padrão) não chama nada. ---
        services.AddScoped<RootBoost.Application.Inbox.IInboxStore, RootBoost.Infrastructure.Inbox.EfInboxStore>();
        services.AddScoped<RootBoost.Application.Inbox.SyncInbox>();
        var inboxReader = config["Inbox:Reader"] ?? "None";
        if (string.Equals(inboxReader, "Fake", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<RootBoost.Application.Inbox.IInboxReader>(sp => new RootBoost.Infrastructure.Inbox.FakeInboxReader(
                (config["Inbox:FakeBrands"] ?? "medlycare").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                sp.GetRequiredService<TimeProvider>()));
        else if (string.Equals(inboxReader, "Meta", StringComparison.OrdinalIgnoreCase))
        {
            Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton(services,
                new RootBoost.Infrastructure.Publishing.SocialAccounts(config));
            services.AddSingleton<RootBoost.Application.Inbox.IInboxReader, RootBoost.Infrastructure.Inbox.InstagramInboxReader>();
        }
        if (!string.Equals(inboxReader, "None", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(config["Inbox:SyncEnabled"], "false", StringComparison.OrdinalIgnoreCase))
            services.AddHostedService<RootBoost.Infrastructure.Inbox.InboxScheduler>();

        // --- Mídia (RB-33): bucket S3 do Railway em produção (Media__Storage=S3), pasta local no dev e nos testes. ---
        var mediaOptions = new RootBoost.Application.Media.MediaOptions();
        config.GetSection(RootBoost.Application.Media.MediaOptions.Section).Bind(mediaOptions);
        services.AddSingleton(mediaOptions);
        if (string.Equals(mediaOptions.Storage, "S3", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<RootBoost.Application.Media.IMediaStorage>(new RootBoost.Infrastructure.Media.S3MediaStorage(mediaOptions));
        else
            services.AddSingleton<RootBoost.Application.Media.IMediaStorage>(new RootBoost.Infrastructure.Media.LocalMediaStorage(mediaOptions));
        services.AddScoped<RootBoost.Application.Media.IMediaAssetStore, EfMediaAssetStore>();
        services.AddScoped<RootBoost.Application.Media.UploadMedia>();

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
