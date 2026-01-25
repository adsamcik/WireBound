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
    public DbSet<SpeedSnapshot> SpeedSnapshots { get; set; } = null!;
    public DbSet<HourlySystemStats> HourlySystemStats { get; set; } = null!;
    public DbSet<DailySystemStats> DailySystemStats { get; set; } = null!;

    /// <summary>
    /// Creates a new instance of WireBoundDbContext with default options.
    /// Uses SQLite database stored in LocalApplicationData folder.
    /// </summary>
    public WireBoundDbContext()
    {
    }

    /// <summary>
    /// Creates a new instance of WireBoundDbContext with the specified options.
    /// Use this constructor for testing with in-memory database.
    /// </summary>
    /// <param name="options">The options to configure the context.</param>
    public WireBoundDbContext(DbContextOptions<WireBoundDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Skip if options are already configured (e.g., for testing with in-memory database)
        if (optionsBuilder.IsConfigured)
            return;

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

        // SpeedSnapshot index for efficient time-based queries
        modelBuilder.Entity<SpeedSnapshot>()
            .HasIndex(s => s.Timestamp);

        // HourlySystemStats index for efficient time-based queries
        modelBuilder.Entity<HourlySystemStats>()
            .HasIndex(h => h.Hour)
            .IsUnique();

        // DailySystemStats index for efficient date-based queries
        modelBuilder.Entity<DailySystemStats>()
            .HasIndex(d => d.Date)
            .IsUnique();

        // Seed default settings
        modelBuilder.Entity<AppSettings>().HasData(new AppSettings { Id = 1 });
    }

    /// <summary>
    /// Applies any necessary schema migrations for existing databases.
    /// EnsureCreated() only creates new tables, it doesn't add columns to existing tables.
    /// This method handles incremental schema changes.
    /// </summary>
    public void ApplyMigrations()
    {
        var connection = Database.GetDbConnection();
        connection.Open();

        try
        {
            // Check and add SpeedUnit column to Settings table if missing (replaces old UseSpeedInBits)
            AddColumnIfNotExists(connection, "Settings", "SpeedUnit", "INTEGER NOT NULL DEFAULT 0");

            // Add StartMinimized column if missing
            AddColumnIfNotExists(connection, "Settings", "StartMinimized", "INTEGER NOT NULL DEFAULT 0");

            // Add per-app tracking columns if missing
            AddColumnIfNotExists(connection, "Settings", "IsPerAppTrackingEnabled", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfNotExists(connection, "Settings", "AppDataRetentionDays", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfNotExists(connection, "Settings", "AppDataAggregateAfterDays", "INTEGER NOT NULL DEFAULT 7");

            // Legacy: remove UseSpeedInBits if it exists and SpeedUnit exists
            // (handled by SQLite ignoring non-existent columns on DROP)
        }
        finally
        {
            connection.Close();
        }
    }

    /// <summary>
    /// Validates that a SQL identifier (table name, column name) contains only safe characters.
    /// This prevents SQL injection attacks when identifiers must be used in dynamic SQL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SECURITY WARNING: SQLite does not support parameterized identifiers (table/column names).
    /// This validation ensures identifiers only contain alphanumeric characters and underscores,
    /// which are safe for direct interpolation into SQL statements.
    /// </para>
    /// <para>
    /// Always call this method before using any identifier in string-interpolated SQL.
    /// </para>
    /// </remarks>
    /// <param name="identifier">The SQL identifier to validate.</param>
    /// <param name="parameterName">The name of the parameter for error messages.</param>
    /// <exception cref="ArgumentException">Thrown when the identifier is null, empty, or contains invalid characters.</exception>
    private static void ValidateSqlIdentifier(string identifier, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);

        // SQLite identifiers should only contain alphanumeric characters and underscores,
        // and must start with a letter or underscore (not a digit)
        if (!System.Text.RegularExpressions.Regex.IsMatch(identifier, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new ArgumentException($"{parameterName} contains invalid characters. Only alphanumeric and underscore allowed, must start with letter or underscore.", parameterName);
    }

    /// <summary>
    /// Adds a column to a table if it doesn't already exist.
    /// Used for incremental schema migrations.
    /// </summary>
    /// <remarks>
    /// SECURITY NOTE: This method uses string interpolation for SQL identifiers because
    /// SQLite does not support parameterized table/column names. All identifiers are
    /// validated using <see cref="ValidateSqlIdentifier"/> before use to prevent SQL injection.
    /// Only call this method with hardcoded, trusted identifier values.
    /// </remarks>
    /// <param name="connection">The database connection.</param>
    /// <param name="table">The table name (must be alphanumeric/underscore only).</param>
    /// <param name="column">The column name (must be alphanumeric/underscore only).</param>
    /// <param name="definition">The column definition (type and constraints).</param>
    private static void AddColumnIfNotExists(System.Data.Common.DbConnection connection, string table, string column, string definition)
    {
        // Validate identifiers to prevent SQL injection
        // Even though we currently only call this with hardcoded values,
        // validation provides defense-in-depth and protects against future misuse
        ValidateSqlIdentifier(table, nameof(table));
        ValidateSqlIdentifier(column, nameof(column));

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";

        var columnExists = false;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
                {
                    columnExists = true;
                    break;
                }
            }
        }

        if (!columnExists)
        {
            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
            alterCommand.ExecuteNonQuery();
        }
    }
}
