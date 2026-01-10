using Microsoft.EntityFrameworkCore;
using WireBound.Models;

namespace WireBound.Tests.TestInfrastructure;

/// <summary>
/// A testable version of WireBoundDbContext that accepts DbContextOptions
/// for in-memory database testing.
/// </summary>
public class TestableDbContext : DbContext
{
    public TestableDbContext(DbContextOptions<TestableDbContext> options)
        : base(options)
    {
    }

    public DbSet<HourlyUsage> HourlyUsages { get; set; } = null!;
    public DbSet<DailyUsage> DailyUsages { get; set; } = null!;
    public DbSet<WeeklyUsage> WeeklyUsages { get; set; } = null!;
    public DbSet<AppSettings> Settings { get; set; } = null!;

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
    }
}
