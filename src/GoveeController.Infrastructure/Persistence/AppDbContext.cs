using GoveeController.Domain.Schedules;
using GoveeController.Domain.Shortcuts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GoveeController.Infrastructure.Persistence;

/// <summary>
/// EF Core database context backing the SQLite-persisted parts of the application. Only
/// <see cref="Shortcut"/> and <see cref="Schedule"/> are persisted — device data always comes live
/// from the Govee API.
/// </summary>
public sealed class AppDbContext : DbContext
{
    /// <summary>Creates the context. Called by the DI container via AddDbContext in ServiceCollectionExtensions.</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>User-defined shortcut presets. See <see cref="Shortcut"/>.</summary>
    public DbSet<Shortcut> Shortcuts => Set<Shortcut>();

    /// <summary>Rules that automatically apply a shortcut on a schedule. See <see cref="Schedule"/>.</summary>
    public DbSet<Schedule> Schedules => Set<Schedule>();

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

            // Self-referencing optional link to the next shortcut in a chain. SetNull (not Cascade)
            // so that deleting a followed shortcut just breaks the link instead of deleting its
            // predecessor too.
            entity.HasOne<Shortcut>()
                .WithMany()
                .HasForeignKey(s => s.NextShortcutId)
                .OnDelete(DeleteBehavior.SetNull);

            // At most one shortcut may point at any given follower — this is what keeps chains
            // linear and makes the 3-shortcut cap unambiguous. SQLite treats NULLs as distinct, so
            // unlinked shortcuts are unaffected.
            entity.HasIndex(s => s.NextShortcutId).IsUnique();

            entity.Property(s => s.NextShortcutDelaySeconds).HasDefaultValue(0);
        });

        modelBuilder.Entity<ShortcutTarget>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.DeviceSku).IsRequired().HasMaxLength(50);
            entity.Property(t => t.DeviceId).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<ShortcutReference>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasOne<Shortcut>()
                .WithMany(s => s.ReferencedShortcuts)
                .HasForeignKey(r => r.ShortcutId)
                .OnDelete(DeleteBehavior.Cascade);

            // Plain FK to the referenced shortcut — no navigation property (same style as
            // Shortcut.NextShortcutId). SetNull (not Cascade) so deleting a referenced shortcut
            // just breaks the link instead of deleting the composite that references it.
            entity.HasOne<Shortcut>()
                .WithMany()
                .HasForeignKey(r => r.ReferencedShortcutId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(r => r.DelaySeconds).HasDefaultValue(0);
        });

        modelBuilder.Entity<Schedule>(entity =>
        {
            entity.HasKey(s => s.Id);

            // Plain FK with no navigation property (same style as Shortcut.NextShortcutId) - Cascade
            // so a schedule never outlives the shortcut it applies. This is a *new* table with a FK
            // pointing at the existing Shortcuts table, not the other way around, so unlike the
            // linked-shortcuts migration this should not trigger SQLite's table-rebuild behavior for
            // Shortcuts - see SCHEDULED-SHORTCUTS-PLAN.md §3.6.
            entity.HasOne<Shortcut>()
                .WithMany()
                .HasForeignKey(s => s.ShortcutId)
                .OnDelete(DeleteBehavior.Cascade);

            // DaysOfWeekMask is a [Flags] enum; EF Core maps enums to their underlying int by
            // default, which is exactly what's wanted here (no need for an explicit conversion).
            // Indexed since the runner's tick queries "everything due" by this column.
            entity.HasIndex(s => s.NextRunAtUtc);

            // SQLite has no concept of DateTimeKind - a value written with Kind=Utc always comes
            // back from a read with Kind=Unspecified. ScheduleService compares this column against
            // TimeProvider.GetUtcNow() (a DateTimeOffset); the implicit DateTime->DateTimeOffset
            // conversion treats an Unspecified Kind as *local* time and converts using the
            // container's TZ offset, silently corrupting the comparison - verified this empirically
            // (a schedule due at 8:38 PM UTC was never picked up as due because the round-tripped
            // value got reinterpreted as 8:38 PM local, i.e. a different UTC instant entirely).
            // Explicitly re-tagging the value as Utc on read closes that gap at the one column where
            // it actually matters (Shortcut.CreatedAtUtc is never compared against anything, so it
            // doesn't need this).
            entity.Property(s => s.NextRunAtUtc)
                .HasConversion(new ValueConverter<DateTime?, DateTime?>(
                    v => v,
                    v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v));
        });
    }
}
