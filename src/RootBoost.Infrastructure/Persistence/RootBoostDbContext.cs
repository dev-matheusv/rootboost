using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class RootBoostDbContext : DbContext
{
    public RootBoostDbContext(DbContextOptions<RootBoostDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();

    /// <summary>Contadores diários de visita de landing (analytics de topo de funil, sem PII).</summary>
    public DbSet<LandingViewDaily> LandingViews => Set<LandingViewDaily>();

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<ProofItem> ProofItems => Set<ProofItem>();

    public DbSet<CustomerPhrase> CustomerPhrases => Set<CustomerPhrase>();

    public DbSet<Publication> Publications => Set<Publication>();

    /// <summary>Senha do dono do Console (só o hash). Ver ConsoleCredential.</summary>
    public DbSet<ConsoleCredential> ConsoleCredentials => Set<ConsoleCredential>();

    /// <summary>Mídias das marcas (o arquivo fica no bucket; aqui só os dados). RB-33.</summary>
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    /// <summary>Snapshot diário de métricas de anúncio (Meta e o que vier). Uma linha por objeto e dia.</summary>
    public DbSet<AdInsightDaily> AdInsights => Set<AdInsightDaily>();

    /// <summary>Avisos de operação pro humano (post que falhou, sync de anúncio, conexão da Meta). RB-58.</summary>
    public DbSet<OpsAlert> OpsAlerts => Set<OpsAlert>();
    public DbSet<CreativeJob> CreativeJobs => Set<CreativeJob>();
    public DbSet<NarrationVoice> NarrationVoices => Set<NarrationVoice>();
    public DbSet<WorkerHeartbeat> WorkerHeartbeats => Set<WorkerHeartbeat>();
    public DbSet<PushDevice> PushDevices => Set<PushDevice>();
    public DbSet<BrandProduct> BrandProducts => Set<BrandProduct>();
    public DbSet<RootBoost.Application.Ai.AiUsageEntry> AiUsage => Set<RootBoost.Application.Ai.AiUsageEntry>();

    /// <summary>Postgres guarda timestamptz e o Npgsql só aceita offset zero: normaliza ao gravar.</summary>
    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> Utc = new(v => v.ToUniversalTime(), v => v);

    protected override void OnModelCreating(ModelBuilder b)
    {
        var view = b.Entity<LandingViewDaily>();
        view.HasKey(x => new { x.ProductKey, x.Lang, x.DateUtc });
        view.Property(x => x.ProductKey).HasMaxLength(64);
        view.Property(x => x.Lang).HasMaxLength(8);
        // Só a data importa: "date" dispensa o Kind do DateTime (timestamptz recusaria Unspecified).
        view.Property(x => x.DateUtc).HasColumnType("date");

        var brand = b.Entity<Brand>();
        brand.HasKey(x => x.Key);
        brand.Property(x => x.Key).HasMaxLength(64);
        brand.Property(x => x.Name).HasMaxLength(120).IsRequired();
        brand.Property(x => x.DefaultLanguage).HasMaxLength(8).IsRequired();
        brand.Property(x => x.VoiceJson).IsRequired().HasDefaultValue("{}");
        brand.Property(x => x.VoiceJson).HasColumnType("jsonb");
        brand.Property(x => x.Privacy).HasConversion<string>().HasMaxLength(16).HasDefaultValue(PersonalDataPolicy.Blur);
        brand.Property(x => x.FictionalMarkers).HasMaxLength(500);
        brand.Ignore(x => x.Voice);

        var proof = b.Entity<ProofItem>();
        proof.HasKey(x => x.Id);
        proof.Property(x => x.BrandKey).HasMaxLength(64).IsRequired();
        proof.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24);
        proof.Property(x => x.Statement).HasMaxLength(1000).IsRequired();
        proof.Property(x => x.Source).HasMaxLength(500).IsRequired();
        proof.HasIndex(x => new { x.BrandKey, x.Active });

        var phrase = b.Entity<CustomerPhrase>();
        phrase.HasKey(x => x.Id);
        phrase.Property(x => x.BrandKey).HasMaxLength(64).IsRequired();
        phrase.Property(x => x.Text).HasMaxLength(1000).IsRequired();
        phrase.Property(x => x.Source).HasMaxLength(500).IsRequired();
        phrase.HasIndex(x => x.BrandKey);
        // Marcas iniciais. Data fixa: HasData precisa de valor determinístico pra não gerar migração a cada build.
        var seededAt = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        brand.HasData(
            new Brand(BrandKeys.Store, "Loja RootBoost", "en-US", seededAt),
            new Brand(BrandKeys.MedlyCare, "MedlyCare", "pt-BR", seededAt),
            // Demonstração em produção (29/10): testar como o cliente usa, com dado fictício, sem nunca publicar.
            new Brand(BrandKeys.Demo, "Clínica Vitta Demo", "pt-BR", new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero)));

        var pub = b.Entity<Publication>();
        pub.HasKey(x => x.Id);
        pub.Property(x => x.BrandKey).HasMaxLength(64).IsRequired();
        pub.Property(x => x.Channel).HasConversion<string>().HasMaxLength(16);
        pub.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        pub.Property(x => x.MediaUrl).HasMaxLength(2048).IsRequired();
        pub.Property(x => x.Caption).HasMaxLength(5000).IsRequired();
        pub.Property(x => x.ApprovedBy).HasMaxLength(320);
        pub.Property(x => x.ExternalId).HasMaxLength(256);
        pub.Property(x => x.PublishedUrl).HasMaxLength(2048);
        pub.Property(x => x.CoverUrl).HasMaxLength(2048);
        pub.Property(x => x.ReminderSentAt).HasConversion(v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v);
        pub.Property(x => x.ShareToFeed).HasDefaultValue(true).HasSentinel(true); // linhas antigas: aparecem no feed; false é gravado de verdade
        pub.HasIndex(x => new { x.Status, x.ScheduledFor });
        pub.HasIndex(x => new { x.BrandKey, x.ScheduledFor });
        // Sem token de concorrência em UpdatedAt de propósito: o Postgres trunca pra microssegundo e o EF compararia
        // com o valor em ticks, dando conflito falso. Contra publicar duas vezes valem o ExternalId gravado antes do
        // publish, o FindPublishedAsync antes de repetir e o agendador numa instância só.

        var ad = b.Entity<AdInsightDaily>();
        ad.HasKey(x => x.Id);
        ad.Property(x => x.BrandKey).HasMaxLength(64).IsRequired();
        ad.Property(x => x.Platform).HasMaxLength(16).IsRequired();
        ad.Property(x => x.AccountId).HasMaxLength(64).IsRequired();
        ad.Property(x => x.Level).HasConversion<string>().HasMaxLength(16);
        ad.Property(x => x.ObjectId).HasMaxLength(64).IsRequired();
        ad.Property(x => x.ObjectName).HasMaxLength(400).IsRequired();
        ad.Property(x => x.CampaignId).HasMaxLength(64);
        ad.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        ad.Property(x => x.Spend).HasPrecision(18, 2);
        // A chave natural garante no banco o que o upsert garante no código: nunca duas linhas do mesmo objeto no mesmo dia.
        ad.HasIndex(x => new { x.Platform, x.AccountId, x.Level, x.ObjectId, x.Date }).IsUnique();
        ad.HasIndex(x => new { x.BrandKey, x.Level, x.Date });
        ad.Property(x => x.FetchedAt).HasConversion(Utc);

        var cred = b.Entity<ConsoleCredential>();
        cred.HasKey(x => x.Email);
        cred.Property(x => x.Email).HasMaxLength(320);
        cred.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        cred.Property(x => x.SourceEnvHash).HasMaxLength(512).IsRequired();
        cred.Property(x => x.UpdatedAt).HasConversion(Utc);

        var alert = b.Entity<OpsAlert>();
        alert.HasKey(x => x.Id);
        alert.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        alert.Property(x => x.Key).HasMaxLength(200).IsRequired();
        alert.Property(x => x.BrandKey).HasMaxLength(64);
        alert.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        alert.Property(x => x.ResolvedBy).HasMaxLength(320);
        alert.Property(x => x.FirstSeenAt).HasConversion(Utc);
        alert.Property(x => x.LastSeenAt).HasConversion(Utc);
        alert.Property(x => x.ResolvedAt).HasConversion(v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v);
        // Um aviso aberto por problema: o índice único parcial impede duplicar mesmo com duas rodadas ao mesmo tempo.
        alert.HasIndex(x => x.Key).IsUnique().HasFilter("\"ResolvedAt\" IS NULL");

        // Estúdio (RB-42): roteiro e falas guardados como JSON do jeito que o worker vai ler (contratos do pacote).
        var job = b.Entity<CreativeJob>();
        job.HasKey(x => x.Id);
        job.Property(x => x.BrandKey).HasMaxLength(64).IsRequired();
        job.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        job.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        job.Property(x => x.Theme).HasMaxLength(300).IsRequired();
        job.Property(x => x.VoiceKey).HasMaxLength(64);
        job.Property(x => x.Script).HasColumnType("jsonb");
        job.Property(x => x.Lines).HasColumnType("jsonb");
        job.Property(x => x.WarningsJson).HasColumnType("jsonb").IsRequired();
        job.Property(x => x.OutputsJson).HasColumnType("jsonb").IsRequired();
        job.Property(x => x.WorkerId).HasMaxLength(100);
        job.Property(x => x.ProgressNote).HasMaxLength(300);
        job.Property(x => x.Error).HasMaxLength(1000);
        job.Property(x => x.CreatedAt).HasConversion(Utc);
        job.Property(x => x.UpdatedAt).HasConversion(Utc);
        job.Property(x => x.LeaseUntil).HasConversion(v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v);
        job.Ignore(x => x.Warnings);
        job.Ignore(x => x.Outputs);
        job.Property(x => x.VoiceTakesJson).HasColumnType("jsonb").IsRequired().HasDefaultValue("[]");
        job.Ignore(x => x.VoiceTakes);
        job.HasIndex(x => new { x.Status, x.CreatedAt });
        job.HasIndex(x => new { x.BrandKey, x.CreatedAt });

        // Sinal de vida dos workers do estúdio (uma linha por computador).
        var beat = b.Entity<WorkerHeartbeat>();
        beat.HasKey(x => x.WorkerId);
        beat.Property(x => x.WorkerId).HasMaxLength(100);
        beat.Property(x => x.Activity).HasMaxLength(300);
        beat.Property(x => x.LastSeenAt).HasConversion(Utc);

        // Vozes das marcas (RB-59): clone com consentimento ou voz do banco; fila do worker como a do estúdio.
        var voice = b.Entity<NarrationVoice>();
        voice.HasKey(x => x.Id);
        voice.Property(x => x.BrandKey).HasMaxLength(64).IsRequired();
        voice.Property(x => x.Key).HasMaxLength(64).IsRequired();
        voice.HasIndex(x => x.Key).IsUnique();
        voice.Property(x => x.Name).HasMaxLength(60).IsRequired();
        voice.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        voice.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        voice.Property(x => x.ReferenceStorageKey).HasMaxLength(300);
        voice.Property(x => x.ReferenceText).HasMaxLength(1000);
        voice.Property(x => x.Instruct).HasMaxLength(200);
        voice.Property(x => x.ConsentBy).HasMaxLength(120);
        voice.Property(x => x.ConsentText).HasMaxLength(600);
        voice.Property(x => x.ConsentAt).HasConversion(v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v);
        voice.Property(x => x.SamplesJson).HasColumnType("jsonb").IsRequired();
        voice.Property(x => x.WorkerId).HasMaxLength(100);
        voice.Property(x => x.Error).HasMaxLength(1000);
        voice.Property(x => x.CreatedAt).HasConversion(Utc);
        voice.Property(x => x.UpdatedAt).HasConversion(Utc);
        voice.Property(x => x.LeaseUntil).HasConversion(v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v);
        voice.Ignore(x => x.Samples);
        voice.HasIndex(x => new { x.Status, x.CreatedAt });
        voice.HasIndex(x => new { x.BrandKey, x.CreatedAt });

        // Aparelhos inscritos no push da PWA (RB-37). Um registro por endereço do navegador.
        var device = b.Entity<PushDevice>();
        device.HasKey(x => x.Id);
        device.Property(x => x.Endpoint).HasMaxLength(1000).IsRequired();
        device.Property(x => x.P256dh).HasMaxLength(200).IsRequired();
        device.Property(x => x.Auth).HasMaxLength(100).IsRequired();
        device.Property(x => x.UserEmail).HasMaxLength(320).IsRequired();
        device.Property(x => x.CreatedAt).HasConversion(Utc);
        device.Property(x => x.LastSentAt).HasConversion(v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v);
        device.HasIndex(x => x.Endpoint).IsUnique();

        // Uso e custo de cada chamada à IA (decisão: tudo registrado, por marca e finalidade).
        var usage = b.Entity<RootBoost.Application.Ai.AiUsageEntry>();
        usage.ToTable("AiUsage");
        usage.HasKey(x => x.Id);
        usage.Property(x => x.BrandKey).HasMaxLength(64);
        usage.Property(x => x.Purpose).HasMaxLength(64).IsRequired();
        usage.Property(x => x.Model).HasMaxLength(64).IsRequired();
        usage.Property(x => x.CostUsd).HasPrecision(18, 6);
        usage.Property(x => x.CreatedAt).HasConversion(Utc);
        usage.HasIndex(x => new { x.BrandKey, x.CreatedAt });

        // Produto da marca (RB-60): um por marca, montado na conversa com a IA.
        var product = b.Entity<BrandProduct>();
        product.HasKey(x => x.Id);
        product.Property(x => x.BrandKey).HasMaxLength(64).IsRequired();
        product.Property(x => x.Name).HasMaxLength(120).IsRequired();
        product.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        product.Property(x => x.Url).HasMaxLength(500);
        product.Property(x => x.AppUrl).HasMaxLength(500);
        product.Property(x => x.Summary).HasMaxLength(1000).IsRequired();
        product.Property(x => x.Audience).HasMaxLength(500).IsRequired();
        product.Property(x => x.FeaturesJson).HasColumnType("jsonb").IsRequired();
        product.Property(x => x.DifferentialsJson).HasColumnType("jsonb").IsRequired();
        product.Property(x => x.Offer).HasMaxLength(300);
        product.Property(x => x.CreatedAt).HasConversion(Utc);
        product.Property(x => x.UpdatedAt).HasConversion(Utc);
        product.Ignore(x => x.Features);
        product.Ignore(x => x.Differentials);
        product.HasIndex(x => x.BrandKey).IsUnique();

        var media = b.Entity<MediaAsset>();
        media.HasKey(x => x.Id);
        media.Property(x => x.BrandKey).HasMaxLength(64).IsRequired();
        media.Property(x => x.FileName).HasMaxLength(160).IsRequired();
        media.Property(x => x.ContentType).HasMaxLength(64).IsRequired();
        media.Property(x => x.StorageKey).HasMaxLength(300).IsRequired();
        media.Property(x => x.Description).HasMaxLength(500);
        media.Property(x => x.Tags).HasMaxLength(300);
        media.Property(x => x.PersonalData).HasConversion<string>().HasMaxLength(16).HasDefaultValue(PersonalDataStatus.Unchecked);
        media.Property(x => x.PersonalDataKinds).HasMaxLength(200);
        media.Property(x => x.RedactionBoxesJson).HasColumnType("jsonb").IsRequired().HasDefaultValue("[]");
        media.Property(x => x.RedactedStorageKey).HasMaxLength(300);
        media.Ignore(x => x.RedactionBoxes);
        media.Property(x => x.FocusJson).HasMaxLength(200);
        media.Property(x => x.MomentsJson).HasColumnType("jsonb").IsRequired().HasDefaultValue("[]");
        media.Ignore(x => x.Moments);
        media.Ignore(x => x.Focus);
        media.Property(x => x.CreatedAt).HasConversion(Utc);
        media.HasIndex(x => new { x.BrandKey, x.CreatedAt });

        var order = b.Entity<Order>();
        order.HasKey(o => o.PaymentId);
        order.Property(o => o.PaymentId).HasMaxLength(128);
        order.Property(o => o.ProductKey).HasMaxLength(64).IsRequired();
        order.Property(o => o.CustomerEmail).HasMaxLength(320);
        order.Property(o => o.Currency).HasMaxLength(3);
        order.Property(o => o.SupplierOrderId).HasMaxLength(128);
        order.Property(o => o.TrackingNumber).HasMaxLength(128);
        order.Property(o => o.Status).HasConversion<string>().HasMaxLength(24);
        // Pedidos de antes das marcas viram da loja na migração (valor padrão da coluna).
        order.Property(o => o.BrandKey).HasMaxLength(64).IsRequired().HasDefaultValue(BrandKeys.Store);
        order.HasIndex(o => o.BrandKey);

        order.Property(o => o.CreatedAt).HasConversion(Utc);
        order.Property(o => o.UpdatedAt).HasConversion(Utc);

        // Fast lookup for the CJ tracking webhook, which arrives keyed by supplier order id.
        order.HasIndex(o => o.SupplierOrderId);

        // ShippingAddress is a value object -> owned type, stored inline on the orders table.
        order.OwnsOne(o => o.ShipTo, addr =>
        {
            addr.Property(a => a.Name).HasColumnName("ShipName").HasMaxLength(200);
            addr.Property(a => a.Line1).HasColumnName("ShipLine1").HasMaxLength(300);
            addr.Property(a => a.Line2).HasColumnName("ShipLine2").HasMaxLength(300);
            addr.Property(a => a.City).HasColumnName("ShipCity").HasMaxLength(120);
            addr.Property(a => a.State).HasColumnName("ShipState").HasMaxLength(120);
            addr.Property(a => a.Zip).HasColumnName("ShipZip").HasMaxLength(32);
            addr.Property(a => a.CountryCode).HasColumnName("ShipCountry").HasMaxLength(2);
            addr.Property(a => a.Phone).HasColumnName("ShipPhone").HasMaxLength(40);
        });
        order.Navigation(o => o.ShipTo).IsRequired();
    }
}
