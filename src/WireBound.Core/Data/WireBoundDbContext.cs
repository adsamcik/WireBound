using Microsoft.EntityFrameworkCore;
using WireBound.Core.Models;

namespace WireBound.Core.Data;

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
