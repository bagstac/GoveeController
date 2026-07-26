using GoveeController.Domain.Shortcuts;
using Microsoft.EntityFrameworkCore;

namespace GoveeController.Infrastructure.Persistence;

/// <summary>
/// EF Core database context backing the SQLite-persisted parts of the application. Only
/// <see cref="Shortcut"/> is persisted — device data always comes live from the Govee API.
/// </summary>
public sealed class AppDbContext : DbContext
{
    /// <summary>Creates the context. Called by the DI container via AddDbContext in ServiceCollectionExtensions.</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>User-defined shortcut presets. See <see cref="Shortcut"/>.</summary>
    public DbSet<Shortcut> Shortcuts => Set<Shortcut>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shortcut>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).IsRequired().HasMaxLength(100);
            entity.HasMany(s => s.Targets)
                .WithOne()
                .HasForeignKey(t => t.ShortcutId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShortcutTarget>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.DeviceSku).IsRequired().HasMaxLength(50);
            entity.Property(t => t.DeviceId).IsRequired().HasMaxLength(100);
        });
    }
}
