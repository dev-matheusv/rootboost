using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class RootBoostDbContext : DbContext
{
    public RootBoostDbContext(DbContextOptions<RootBoostDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var order = b.Entity<Order>();
        order.HasKey(o => o.PaymentId);
        order.Property(o => o.PaymentId).HasMaxLength(128);
        order.Property(o => o.ProductKey).HasMaxLength(64).IsRequired();
        order.Property(o => o.CustomerEmail).HasMaxLength(320);
        order.Property(o => o.Currency).HasMaxLength(3);
        order.Property(o => o.SupplierOrderId).HasMaxLength(128);
        order.Property(o => o.TrackingNumber).HasMaxLength(128);
        order.Property(o => o.Status).HasConversion<string>().HasMaxLength(24);

        // SQLite can't ORDER BY a DateTimeOffset column; store it as a sortable binary long.
        var dto = new DateTimeOffsetToBinaryConverter();
        order.Property(o => o.CreatedAt).HasConversion(dto);
        order.Property(o => o.UpdatedAt).HasConversion(dto);

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
