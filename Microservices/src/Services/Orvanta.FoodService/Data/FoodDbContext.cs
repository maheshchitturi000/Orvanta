using Microsoft.EntityFrameworkCore;
using Orvanta.FoodService.Domain;

namespace Orvanta.FoodService.Data;

public sealed class FoodDbContext(DbContextOptions<FoodDbContext> options) : DbContext(options)
{
    public DbSet<ProviderConnection> ProviderConnections => Set<ProviderConnection>();
    public DbSet<PendingAuthorization> PendingAuthorizations => Set<PendingAuthorization>();
    public DbSet<OAuthClientRegistration> OAuthClientRegistrations => Set<OAuthClientRegistration>();
    public DbSet<SavedAddress> SavedAddresses => Set<SavedAddress>();
    public DbSet<FoodOrder> FoodOrders => Set<FoodOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderConnection>(e =>
        {
            e.ToTable("ProviderConnections");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(40).IsRequired();
            e.HasIndex(x => new { x.UserId, x.Provider }).IsUnique();
            e.Property(x => x.Scope).HasMaxLength(200);
        });

        modelBuilder.Entity<PendingAuthorization>(e =>
        {
            e.ToTable("PendingAuthorizations");
            e.HasKey(x => x.StateHash);
            e.Property(x => x.StateHash).HasMaxLength(64);
            e.Property(x => x.Provider).HasMaxLength(40).IsRequired();
            e.Property(x => x.RedirectUri).HasMaxLength(500).IsRequired();
        });

        modelBuilder.Entity<OAuthClientRegistration>(e =>
        {
            e.ToTable("OAuthClientRegistrations");
            e.HasKey(x => new { x.Provider, x.RedirectUri });
            e.Property(x => x.Provider).HasMaxLength(40);
            e.Property(x => x.RedirectUri).HasMaxLength(500);
            e.Property(x => x.ClientId).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<SavedAddress>(e =>
        {
            e.ToTable("SavedAddresses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(40).IsRequired();
            e.Property(x => x.ExternalId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Label).HasMaxLength(100);
            e.Property(x => x.AddressLine).HasMaxLength(500);
            e.Property(x => x.PhoneNumber).HasMaxLength(20);
            e.HasIndex(x => new { x.UserId, x.Provider, x.ExternalId }).IsUnique();
        });

        modelBuilder.Entity<FoodOrder>(e =>
        {
            e.ToTable("FoodOrders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(40).IsRequired();
            e.Property(x => x.ProviderOrderId).HasMaxLength(100);
            e.Property(x => x.RestaurantId).HasMaxLength(100);
            e.Property(x => x.RestaurantName).HasMaxLength(200);
            e.Property(x => x.MenuItemId).HasMaxLength(100);
            e.Property(x => x.ItemName).HasMaxLength(200);
            e.Property(x => x.TotalAmount).HasPrecision(10, 2);
            e.Property(x => x.PaymentMethod).HasMaxLength(30);
            e.Property(x => x.AddressId).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(30);
            e.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        });
    }
}
