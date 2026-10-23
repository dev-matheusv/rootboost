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
        // Marcas iniciais. Data fixa: HasData precisa de valor determinístico pra não gerar migração a cada build.
        var seededAt = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        brand.HasData(
            new Brand(BrandKeys.Store, "Loja RootBoost", "en-US", seededAt),
            new Brand(BrandKeys.MedlyCare, "MedlyCare", "pt-BR", seededAt));

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
