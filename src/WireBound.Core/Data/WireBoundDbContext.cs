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
    public DbSet<AddressUsageRecord> AddressUsageRecords { get; set; } = null!;

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

        // AddressUsageRecord indexes for per-address network tracking
        modelBuilder.Entity<AddressUsageRecord>()
            .HasIndex(a => new { a.Timestamp, a.RemoteAddress, a.Granularity })
            .IsUnique();

        modelBuilder.Entity<AddressUsageRecord>()
            .HasIndex(a => a.RemoteAddress);

        modelBuilder.Entity<AddressUsageRecord>()
            .HasIndex(a => new { a.Granularity, a.Timestamp });

        modelBuilder.Entity<AddressUsageRecord>()
            .HasIndex(a => a.AppIdentifier);

        // Seed default settings
        modelBuilder.Entity<AppSettings>().HasData(new AppSettings { Id = 1 });
    }

    /// <summary>
    /// Applies any necessary schema migrations for existing databases.
    /// EnsureCreated() only creates new tables, it doesn't add columns to existing tables.
    /// This method handles incremental schema changes by ensuring all tables and columns exist.
    /// All operations are idempotent — safe to run on any database version.
    /// </summary>
    public void ApplyMigrations()
    {
        var connection = Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen)
            connection.Open();

        try
        {
            // Phase 1: Ensure all tables exist (for tables added after initial release)
            CreateTableIfNotExists(connection, "AppUsageRecords", """
                CREATE TABLE IF NOT EXISTS AppUsageRecords (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    AppIdentifier TEXT NOT NULL DEFAULT '',
                    AppName TEXT NOT NULL DEFAULT '',
                    ExecutablePath TEXT NOT NULL DEFAULT '',
                    ProcessName TEXT NOT NULL DEFAULT '',
                    Timestamp TEXT NOT NULL DEFAULT '0001-01-01',
                    Granularity INTEGER NOT NULL DEFAULT 0,
                    BytesReceived INTEGER NOT NULL DEFAULT 0,
                    BytesSent INTEGER NOT NULL DEFAULT 0,
                    PeakDownloadSpeed INTEGER NOT NULL DEFAULT 0,
                    PeakUploadSpeed INTEGER NOT NULL DEFAULT 0,
                    LastUpdated TEXT NOT NULL DEFAULT '0001-01-01'
                )
                """);

            CreateTableIfNotExists(connection, "HourlySystemStats", """
                CREATE TABLE IF NOT EXISTS HourlySystemStats (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Hour TEXT NOT NULL DEFAULT '0001-01-01',
                    AvgCpuPercent REAL NOT NULL DEFAULT 0,
                    MaxCpuPercent REAL NOT NULL DEFAULT 0,
                    MinCpuPercent REAL NOT NULL DEFAULT 0,
                    AvgMemoryPercent REAL NOT NULL DEFAULT 0,
                    MaxMemoryPercent REAL NOT NULL DEFAULT 0,
                    AvgMemoryUsedBytes INTEGER NOT NULL DEFAULT 0,
                    AvgGpuPercent REAL,
                    MaxGpuPercent REAL,
                    AvgGpuMemoryPercent REAL
                )
                """);

            CreateTableIfNotExists(connection, "DailySystemStats", """
                CREATE TABLE IF NOT EXISTS DailySystemStats (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT NOT NULL DEFAULT '0001-01-01',
                    AvgCpuPercent REAL NOT NULL DEFAULT 0,
                    MaxCpuPercent REAL NOT NULL DEFAULT 0,
                    AvgMemoryPercent REAL NOT NULL DEFAULT 0,
                    MaxMemoryPercent REAL NOT NULL DEFAULT 0,
                    PeakMemoryUsedBytes INTEGER NOT NULL DEFAULT 0,
                    AvgGpuPercent REAL,
                    MaxGpuPercent REAL
                )
                """);

            CreateTableIfNotExists(connection, "AddressUsageRecords", """
                CREATE TABLE IF NOT EXISTS AddressUsageRecords (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    RemoteAddress TEXT NOT NULL DEFAULT '',
                    Hostname TEXT,
                    PrimaryPort INTEGER NOT NULL DEFAULT 0,
                    Protocol TEXT NOT NULL DEFAULT 'TCP',
                    Timestamp TEXT NOT NULL DEFAULT '0001-01-01',
                    Granularity INTEGER NOT NULL DEFAULT 0,
                    BytesSent INTEGER NOT NULL DEFAULT 0,
                    BytesReceived INTEGER NOT NULL DEFAULT 0,
                    ConnectionCount INTEGER NOT NULL DEFAULT 0,
                    PeakSendSpeed INTEGER NOT NULL DEFAULT 0,
                    PeakReceiveSpeed INTEGER NOT NULL DEFAULT 0,
                    AppIdentifier TEXT,
                    LastUpdated TEXT NOT NULL DEFAULT '0001-01-01'
                )
                """);

            // Phase 2: Ensure all columns exist on all tables.
            // Uses batch checks (one PRAGMA per table) for efficiency.
            // All calls are no-ops when the column already exists.
            EnsureColumnsExist(connection, "Settings",
                ("PollingIntervalMs", "INTEGER NOT NULL DEFAULT 1000"),
                ("SaveIntervalSeconds", "INTEGER NOT NULL DEFAULT 60"),
                ("StartWithWindows", "INTEGER NOT NULL DEFAULT 0"),
                ("MinimizeToTray", "INTEGER NOT NULL DEFAULT 1"),
                ("UseIpHelperApi", "INTEGER NOT NULL DEFAULT 0"),
                ("SelectedAdapterId", "TEXT NOT NULL DEFAULT ''"),
                ("DataRetentionDays", "INTEGER NOT NULL DEFAULT 365"),
                ("Theme", "TEXT NOT NULL DEFAULT 'Dark'"),
                ("SpeedUnit", "INTEGER NOT NULL DEFAULT 0"),
                ("StartMinimized", "INTEGER NOT NULL DEFAULT 0"),
                ("IsPerAppTrackingEnabled", "INTEGER NOT NULL DEFAULT 0"),
                ("AppDataRetentionDays", "INTEGER NOT NULL DEFAULT 0"),
                ("AppDataAggregateAfterDays", "INTEGER NOT NULL DEFAULT 7"),
                ("ShowSystemMetricsInHeader", "INTEGER NOT NULL DEFAULT 1"),
                ("ShowCpuOverlayByDefault", "INTEGER NOT NULL DEFAULT 0"),
                ("ShowMemoryOverlayByDefault", "INTEGER NOT NULL DEFAULT 0"),
                ("ShowGpuMetrics", "INTEGER NOT NULL DEFAULT 1"),
                ("DefaultTimeRange", "TEXT NOT NULL DEFAULT 'FiveMinutes'"),
                ("PerformanceModeEnabled", "INTEGER NOT NULL DEFAULT 0"),
                ("ChartUpdateIntervalMs", "INTEGER NOT NULL DEFAULT 1000"),
                ("DefaultInsightsPeriod", "TEXT NOT NULL DEFAULT 'ThisWeek'"),
                ("ShowCorrelationInsights", "INTEGER NOT NULL DEFAULT 1"),
                ("CheckForUpdates", "INTEGER NOT NULL DEFAULT 1"),
                ("AutoDownloadUpdates", "INTEGER NOT NULL DEFAULT 1"));

            EnsureColumnsExist(connection, "HourlyUsages",
                ("Hour", "TEXT NOT NULL DEFAULT '0001-01-01'"),
                ("AdapterId", "TEXT NOT NULL DEFAULT ''"),
                ("BytesReceived", "INTEGER NOT NULL DEFAULT 0"),
                ("BytesSent", "INTEGER NOT NULL DEFAULT 0"),
                ("PeakDownloadSpeed", "INTEGER NOT NULL DEFAULT 0"),
                ("PeakUploadSpeed", "INTEGER NOT NULL DEFAULT 0"),
                ("LastUpdated", "TEXT NOT NULL DEFAULT '0001-01-01'"));

            EnsureColumnsExist(connection, "DailyUsages",
                ("Date", "TEXT NOT NULL DEFAULT '0001-01-01'"),
                ("AdapterId", "TEXT NOT NULL DEFAULT ''"),
                ("BytesReceived", "INTEGER NOT NULL DEFAULT 0"),
                ("BytesSent", "INTEGER NOT NULL DEFAULT 0"),
                ("PeakDownloadSpeed", "INTEGER NOT NULL DEFAULT 0"),
                ("PeakUploadSpeed", "INTEGER NOT NULL DEFAULT 0"),
                ("LastUpdated", "TEXT NOT NULL DEFAULT '0001-01-01'"));

            EnsureColumnsExist(connection, "SpeedSnapshots",
                ("Timestamp", "TEXT NOT NULL DEFAULT '0001-01-01'"),
                ("DownloadSpeedBps", "INTEGER NOT NULL DEFAULT 0"),
                ("UploadSpeedBps", "INTEGER NOT NULL DEFAULT 0"));

            EnsureColumnsExist(connection, "AppUsageRecords",
                ("AppIdentifier", "TEXT NOT NULL DEFAULT ''"),
                ("AppName", "TEXT NOT NULL DEFAULT ''"),
                ("ExecutablePath", "TEXT NOT NULL DEFAULT ''"),
                ("ProcessName", "TEXT NOT NULL DEFAULT ''"),
                ("Timestamp", "TEXT NOT NULL DEFAULT '0001-01-01'"),
                ("Granularity", "INTEGER NOT NULL DEFAULT 0"),
                ("BytesReceived", "INTEGER NOT NULL DEFAULT 0"),
                ("BytesSent", "INTEGER NOT NULL DEFAULT 0"),
                ("PeakDownloadSpeed", "INTEGER NOT NULL DEFAULT 0"),
                ("PeakUploadSpeed", "INTEGER NOT NULL DEFAULT 0"),
                ("LastUpdated", "TEXT NOT NULL DEFAULT '0001-01-01'"));

            EnsureColumnsExist(connection, "HourlySystemStats",
                ("Hour", "TEXT NOT NULL DEFAULT '0001-01-01'"),
                ("AvgCpuPercent", "REAL NOT NULL DEFAULT 0"),
                ("MaxCpuPercent", "REAL NOT NULL DEFAULT 0"),
                ("MinCpuPercent", "REAL NOT NULL DEFAULT 0"),
                ("AvgMemoryPercent", "REAL NOT NULL DEFAULT 0"),
                ("MaxMemoryPercent", "REAL NOT NULL DEFAULT 0"),
                ("AvgMemoryUsedBytes", "INTEGER NOT NULL DEFAULT 0"),
                ("AvgGpuPercent", "REAL"),
                ("MaxGpuPercent", "REAL"),
                ("AvgGpuMemoryPercent", "REAL"));

            EnsureColumnsExist(connection, "DailySystemStats",
                ("Date", "TEXT NOT NULL DEFAULT '0001-01-01'"),
                ("AvgCpuPercent", "REAL NOT NULL DEFAULT 0"),
                ("MaxCpuPercent", "REAL NOT NULL DEFAULT 0"),
                ("AvgMemoryPercent", "REAL NOT NULL DEFAULT 0"),
                ("MaxMemoryPercent", "REAL NOT NULL DEFAULT 0"),
                ("PeakMemoryUsedBytes", "INTEGER NOT NULL DEFAULT 0"),
                ("AvgGpuPercent", "REAL"),
                ("MaxGpuPercent", "REAL"));

            EnsureColumnsExist(connection, "AddressUsageRecords",
                ("RemoteAddress", "TEXT NOT NULL DEFAULT ''"),
                ("Hostname", "TEXT"),
                ("PrimaryPort", "INTEGER NOT NULL DEFAULT 0"),
                ("Protocol", "TEXT NOT NULL DEFAULT 'TCP'"),
                ("Timestamp", "TEXT NOT NULL DEFAULT '0001-01-01'"),
                ("Granularity", "INTEGER NOT NULL DEFAULT 0"),
                ("BytesSent", "INTEGER NOT NULL DEFAULT 0"),
                ("BytesReceived", "INTEGER NOT NULL DEFAULT 0"),
                ("ConnectionCount", "INTEGER NOT NULL DEFAULT 0"),
                ("PeakSendSpeed", "INTEGER NOT NULL DEFAULT 0"),
                ("PeakReceiveSpeed", "INTEGER NOT NULL DEFAULT 0"),
                ("AppIdentifier", "TEXT"),
                ("LastUpdated", "TEXT NOT NULL DEFAULT '0001-01-01'"));
        }
        finally
        {
            if (!wasOpen)
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
    /// Ensures all specified columns exist in a table, adding any that are missing.
    /// Performs a single PRAGMA table_info query per table for efficiency.
    /// </summary>
    /// <remarks>
    /// SECURITY NOTE: This method uses string interpolation for SQL identifiers because
    /// SQLite does not support parameterized table/column names. All identifiers are
    /// validated using <see cref="ValidateSqlIdentifier"/> before use to prevent SQL injection.
    /// Only call this method with hardcoded, trusted identifier values.
    /// </remarks>
    private static void EnsureColumnsExist(
        System.Data.Common.DbConnection connection,
        string table,
        params (string Column, string Definition)[] columns)
    {
        ValidateSqlIdentifier(table, nameof(table));

        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var infoCmd = connection.CreateCommand())
        {
            infoCmd.CommandText = $"PRAGMA table_info({table})";
            using var reader = infoCmd.ExecuteReader();
            while (reader.Read())
                existingColumns.Add(reader.GetString(1));
        }

        foreach (var (column, definition) in columns)
        {
            ValidateSqlIdentifier(column, nameof(column));
            if (!existingColumns.Contains(column))
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
                alterCmd.ExecuteNonQuery();
            }
        }
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

    /// <summary>
    /// Creates a table if it doesn't already exist in the database.
    /// Used for incremental schema migrations when new entity types are added.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="tableName">The table name (must be alphanumeric/underscore only).</param>
    /// <param name="createSql">The full CREATE TABLE IF NOT EXISTS SQL statement.</param>
    private static void CreateTableIfNotExists(System.Data.Common.DbConnection connection, string tableName, string createSql)
    {
        ValidateSqlIdentifier(tableName, nameof(tableName));

        using var command = connection.CreateCommand();
        command.CommandText = createSql;
        command.ExecuteNonQuery();
    }
}
