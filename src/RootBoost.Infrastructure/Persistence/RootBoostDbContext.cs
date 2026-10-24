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

    /// <summary>Snapshot diário de métricas de anúncio (Meta e o que vier). Uma linha por objeto e dia.</summary>
    public DbSet<AdInsightDaily> AdInsights => Set<AdInsightDaily>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var view = b.Entity<LandingViewDaily>();
        view.HasKey(x => new { x.ProductKey, x.Lang, x.DateUtc });
        view.Property(x => x.ProductKey).HasMaxLength(64);
        view.Property(x => x.Lang).HasMaxLength(8);
        // Só a data importa. No Postgres vira "date", o que dispensa o Kind do DateTime
        // (dado importado do SQLite chega como Unspecified e o timestamptz recusaria).
        if (!Database.IsSqlite()) view.Property(x => x.DateUtc).HasColumnType("date");

        var brand = b.Entity<Brand>();
        brand.HasKey(x => x.Key);
        brand.Property(x => x.Key).HasMaxLength(64);
        brand.Property(x => x.Name).HasMaxLength(120).IsRequired();
        brand.Property(x => x.DefaultLanguage).HasMaxLength(8).IsRequired();
        brand.Property(x => x.VoiceJson).IsRequired().HasDefaultValue("{}");
        if (!Database.IsSqlite()) brand.Property(x => x.VoiceJson).HasColumnType("jsonb");
        brand.Ignore(x => x.Voice);

        var proof = b.Entity<ProofItem>();
        proof.HasKey(x => x.Id);
        proof.Property(x => x.BrandKey).HasMaxLength(64).IsRequired();
        proof.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24);
        proof.Property(x => x.Statement).HasMaxLength(1000).IsRequired();
        proof.Property(x => x.Source).HasMaxLength(500).IsRequired();
        proof.HasIndex(x => new { x.BrandKey, x.Active });
        // Mesmo motivo dos pedidos: o SQLite não ordena por DateTimeOffset.
        if (Database.IsSqlite()) proof.Property(x => x.CreatedAt).HasConversion(new DateTimeOffsetToBinaryConverter());

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
            new Brand(BrandKeys.MedlyCare, "MedlyCare", "pt-BR", seededAt));

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
        pub.HasIndex(x => new { x.Status, x.ScheduledFor });
        pub.HasIndex(x => new { x.BrandKey, x.ScheduledFor });
        // Sem token de concorrência em UpdatedAt de propósito: o Postgres trunca pra microssegundo e o EF compararia
        // com o valor em ticks, dando conflito falso. Contra publicar duas vezes valem o ExternalId gravado antes do
        // publish, o FindPublishedAsync antes de repetir e o agendador numa instância só.
        if (Database.IsSqlite())
        {
            var bin = new DateTimeOffsetToBinaryConverter();
            pub.Property(x => x.ScheduledFor).HasConversion(bin);
            pub.Property(x => x.CreatedAt).HasConversion(bin);
            pub.Property(x => x.UpdatedAt).HasConversion(bin);
            pub.Property(x => x.ApprovedAt).HasConversion(bin);
            pub.Property(x => x.PublishingStartedAt).HasConversion(bin);
        }

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
        if (Database.IsSqlite())
        {
            ad.Property(x => x.FetchedAt).HasConversion(new DateTimeOffsetToBinaryConverter());
            // SQLite não soma nem compara decimal no servidor; guardado como double basta pra dinheiro com 2 casas.
            ad.Property(x => x.Spend).HasConversion<double>();
        }
        else
        {
            ad.Property(x => x.FetchedAt).HasConversion(new ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v));
        }

        var cred = b.Entity<ConsoleCredential>();
        cred.HasKey(x => x.Email);
        cred.Property(x => x.Email).HasMaxLength(320);
        cred.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        cred.Property(x => x.SourceEnvHash).HasMaxLength(512).IsRequired();
        if (Database.IsSqlite()) cred.Property(x => x.UpdatedAt).HasConversion(new DateTimeOffsetToBinaryConverter());
        else cred.Property(x => x.UpdatedAt).HasConversion(new ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v));

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

        if (Database.IsSqlite())
        {
            // SQLite can't ORDER BY a DateTimeOffset column; store it as a sortable binary long.
            var dto = new DateTimeOffsetToBinaryConverter();
            order.Property(o => o.CreatedAt).HasConversion(dto);
            order.Property(o => o.UpdatedAt).HasConversion(dto);
        }
        else
        {
            // Postgres guarda timestamptz e o Npgsql só aceita offset zero: normaliza ao gravar.
            var utc = new ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v);
            order.Property(o => o.CreatedAt).HasConversion(utc);
            order.Property(o => o.UpdatedAt).HasConversion(utc);
        }

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
