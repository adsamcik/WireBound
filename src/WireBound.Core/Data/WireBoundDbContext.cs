using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using WireBound.Core.Models;

namespace WireBound.Core.Data;

/// <summary>
/// Database context for WireBound data storage.
/// Note: EF Core has limited trimming/AOT support. The RequiresUnreferencedCode warning is suppressed
/// because we don't use PublishTrimmed=true in production and EF Core works fine at runtime.
/// </summary>
[SuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "EF Core is not fully compatible with trimming, but works fine at runtime without trimming enabled")]
public sealed class WireBoundDbContext : DbContext
{
    public DbSet<HourlyUsage> HourlyUsages { get; set; } = null!;
    public DbSet<DailyUsage> DailyUsages { get; set; } = null!;
    public DbSet<AppSettings> Settings { get; set; } = null!;
    public DbSet<AppUsageRecord> AppUsageRecords { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WireBound",
            "wirebound.db");

        // Ensure directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // HourlyUsage indexes
        modelBuilder.Entity<HourlyUsage>()
            .HasIndex(h => new { h.Hour, h.AdapterId })
            .IsUnique();

        modelBuilder.Entity<HourlyUsage>()
            .HasIndex(h => h.Hour);

        // DailyUsage indexes
        modelBuilder.Entity<DailyUsage>()
            .HasIndex(d => new { d.Date, d.AdapterId })
            .IsUnique();

        modelBuilder.Entity<DailyUsage>()
            .HasIndex(d => d.Date);

        // AppUsageRecord indexes for per-app network tracking
        modelBuilder.Entity<AppUsageRecord>()
            .HasIndex(a => new { a.Timestamp, a.AppIdentifier, a.Granularity })
            .IsUnique();

        modelBuilder.Entity<AppUsageRecord>()
            .HasIndex(a => a.Timestamp);

        modelBuilder.Entity<AppUsageRecord>()
            .HasIndex(a => a.AppIdentifier);

        modelBuilder.Entity<AppUsageRecord>()
            .HasIndex(a => new { a.Granularity, a.Timestamp });

        // Seed default settings
        modelBuilder.Entity<AppSettings>().HasData(new AppSettings { Id = 1 });
    }
}
