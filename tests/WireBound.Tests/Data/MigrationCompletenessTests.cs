using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WireBound.Core.Data;
using WireBound.Core.Models;

namespace WireBound.Tests.Data;

/// <summary>
/// Verifies that ApplyMigrations() handles all model columns,
/// so that any pre-existing database can be fully migrated.
/// If a new property is added to a model without a corresponding
/// migration in ApplyMigrations(), these tests will fail.
///
/// Three layers of protection:
///   1. Schema completeness — every column from EnsureCreated exists after ApplyMigrations
///   2. Per-table migration — each table individually upgradable from PK-only stub
///   3. Functional roundtrip — EF Core can actually read/write every entity after migration
/// </summary>
public class MigrationCompletenessTests
{
    #region Schema Completeness Tests

    [Test]
    [MethodDataSource(nameof(GetEntityTables))]
    public async Task ApplyMigrations_AddsAllColumns_ForTable(string tableName, List<string> expectedColumns)
    {
        // Arrange: create a minimal DB with only PK columns (simulates oldest possible database)
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = $"CREATE TABLE [{tableName}] (Id INTEGER PRIMARY KEY AUTOINCREMENT)";
        await createCmd.ExecuteNonQueryAsync();

        // Settings table needs a seeded row (matches OnModelCreating HasData)
        if (tableName == "Settings")
        {
            await using var seedCmd = connection.CreateCommand();
            seedCmd.CommandText = "INSERT INTO Settings (Id) VALUES (1)";
            await seedCmd.ExecuteNonQueryAsync();
        }

        // Also create any other tables that ApplyMigrations may reference
        await EnsureAllTablesExist(connection, tableName);

        var options = new DbContextOptionsBuilder<WireBoundDbContext>()
            .UseSqlite(connection)
            .Options;

        // Act: run migrations on the minimal DB
        await using var context = new WireBoundDbContext(options);
        context.ApplyMigrations();

        // Reopen connection (ApplyMigrations closes it)
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        // Assert: verify all expected columns now exist
        var actualColumns = await GetColumnsAsync(connection, tableName);
        foreach (var expected in expectedColumns)
        {
            actualColumns.Should().Contain(expected,
                $"Column '{expected}' is missing from table '{tableName}' after ApplyMigrations(). " +
                $"Add it via EnsureColumnsExist() in WireBoundDbContext.ApplyMigrations().");
        }
    }

    [Test]
    public async Task ApplyMigrations_ProducesSameSchema_AsEnsureCreated()
    {
        // Arrange: create a fresh DB with EnsureCreated (the "truth")
        using var freshConnection = new SqliteConnection("Data Source=:memory:");
        await freshConnection.OpenAsync();

        var freshOptions = new DbContextOptionsBuilder<WireBoundDbContext>()
            .UseSqlite(freshConnection)
            .Options;

        await using (var freshContext = new WireBoundDbContext(freshOptions))
        {
            await freshContext.Database.EnsureCreatedAsync();
        }

        var expectedSchema = await GetSchemaAsync(freshConnection);

        // Arrange: create a minimal DB with only PK columns per table
        using var oldConnection = new SqliteConnection("Data Source=:memory:");
        await oldConnection.OpenAsync();

        foreach (var tableName in expectedSchema.Keys)
        {
            await using var cmd = oldConnection.CreateCommand();
            cmd.CommandText = $"CREATE TABLE [{tableName}] (Id INTEGER PRIMARY KEY AUTOINCREMENT)";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var seedCmd = oldConnection.CreateCommand())
        {
            seedCmd.CommandText = "INSERT INTO Settings (Id) VALUES (1)";
            await seedCmd.ExecuteNonQueryAsync();
        }

        var oldOptions = new DbContextOptionsBuilder<WireBoundDbContext>()
            .UseSqlite(oldConnection)
            .Options;

        // Act: run only ApplyMigrations (no EnsureCreated)
        await using (var oldContext = new WireBoundDbContext(oldOptions))
        {
            oldContext.ApplyMigrations();
        }

        // Reopen connection (ApplyMigrations closes it)
        if (oldConnection.State != System.Data.ConnectionState.Open)
            await oldConnection.OpenAsync();

        var migratedSchema = await GetSchemaAsync(oldConnection);

        // Assert: every column from EnsureCreated must exist after ApplyMigrations
        foreach (var (tableName, expectedColumns) in expectedSchema)
        {
            migratedSchema.Should().ContainKey(tableName,
                $"Table '{tableName}' is missing after ApplyMigrations(). " +
                $"Add a CreateTableIfNotExists() call in WireBoundDbContext.ApplyMigrations().");

            foreach (var column in expectedColumns)
            {
                migratedSchema[tableName].Should().Contain(column,
                    $"Column '{column}' missing from table '{tableName}' after ApplyMigrations(). " +
                    $"Add it via EnsureColumnsExist() in WireBoundDbContext.ApplyMigrations().");
            }
        }
    }

    #endregion

    #region Functional Roundtrip Tests — EF Core Must Actually Work After Migration

    /// <summary>
    /// The critical guard test: simulates upgrading an old database (PK-only stubs),
    /// runs ApplyMigrations, then verifies EF Core can read the Settings entity.
    /// This is the exact scenario that caused the "no such column: s.AutoDownloadUpdates" bug.
    /// </summary>
    [Test]
    public async Task ApplyMigrations_SettingsReadable_AfterMigrationFromOldDatabase()
    {
        await using var context = await CreateMigratedContextAsync();

        // Act: read settings — this is what failed with "no such column" errors
        var settings = await context.Settings.FirstOrDefaultAsync();

        // Assert
        settings.Should().NotBeNull("Settings row should exist after migration (seeded in OnModelCreating)");
        settings!.Id.Should().Be(1);
    }

    /// <summary>
    /// Verifies that ALL DbSet queries work after migration from an old database.
    /// Each SELECT triggers EF Core to generate SQL with every mapped column —
    /// any missing column will cause a SqliteException.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(GetDbSetQueryActions))]
    public async Task ApplyMigrations_EntityQueryable_AfterMigrationFromOldDatabase(
        string entityName,
        Func<WireBoundDbContext, Task> queryAction)
    {
        await using var context = await CreateMigratedContextAsync();

        // Act & Assert: the query itself must not throw SqliteException
        var action = () => queryAction(context);
        await action.Should().NotThrowAsync(
            $"EF Core query for '{entityName}' failed after ApplyMigrations(). " +
            $"A column referenced by the entity model is likely missing from ApplyMigrations(). " +
            $"Check EnsureColumnsExist() calls for the '{entityName}' table.");
    }

    /// <summary>
    /// Verifies that Settings can be written after migration — catches NOT NULL
    /// constraints or missing default values that would break persistence.
    /// </summary>
    [Test]
    public async Task ApplyMigrations_SettingsWritable_AfterMigrationFromOldDatabase()
    {
        await using var context = await CreateMigratedContextAsync();

        // Act: modify and save settings
        var settings = await context.Settings.FirstAsync();
        settings.PollingIntervalMs = 2000;
        settings.Theme = "Light";
        var action = () => context.SaveChangesAsync();

        // Assert
        await action.Should().NotThrowAsync(
            "Saving Settings failed after migration. Check column defaults and NOT NULL constraints.");
    }

    /// <summary>
    /// Verifies that new entities can be inserted into every table after migration.
    /// Catches schema issues like missing columns or broken constraints.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(GetDbSetInsertActions))]
    public async Task ApplyMigrations_EntityInsertable_AfterMigrationFromOldDatabase(
        string entityName,
        Func<WireBoundDbContext, Task> insertAction)
    {
        await using var context = await CreateMigratedContextAsync();

        // Act & Assert: insert must not throw
        var action = () => insertAction(context);
        await action.Should().NotThrowAsync(
            $"Inserting '{entityName}' failed after ApplyMigrations(). " +
            $"Check column defaults and NOT NULL constraints in the migration.");
    }

    #endregion

    #region Idempotency Tests

    [Test]
    public async Task ApplyMigrations_Idempotent_RunningTwiceDoesNotThrow()
    {
        await using var context = await CreateMigratedContextAsync();

        // Act: run migrations a second time on the already-migrated database
        var action = () =>
        {
            context.ApplyMigrations();
            return Task.CompletedTask;
        };

        // Assert
        await action.Should().NotThrowAsync(
            "ApplyMigrations() should be idempotent — running twice must not throw.");
    }

    [Test]
    public async Task ApplyMigrations_OnFreshDatabase_DoesNotThrow()
    {
        // Arrange: fresh database created by EnsureCreated (all columns already exist)
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<WireBoundDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new WireBoundDbContext(options);
        await context.Database.EnsureCreatedAsync();

        // Act: ApplyMigrations on a fresh DB should be a no-op
        var action = () =>
        {
            context.ApplyMigrations();
            return Task.CompletedTask;
        };

        // Assert
        await action.Should().NotThrowAsync(
            "ApplyMigrations() must be safe to run on a fresh database created by EnsureCreated().");
    }

    #endregion

    #region Test Data Providers

    /// <summary>
    /// Provides test data: each entity table with its expected columns from a fresh EnsureCreated schema.
    /// </summary>
    public static async Task<List<Func<(string TableName, List<string> ExpectedColumns)>>> GetEntityTables()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<WireBoundDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new WireBoundDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var schema = await GetSchemaAsync(connection);
        return schema.Select(kvp =>
        {
            var tableName = kvp.Key;
            var columns = kvp.Value;
            return new Func<(string, List<string>)>(() => (tableName, new List<string>(columns)));
        }).ToList();
    }

    /// <summary>
    /// Provides a query action for each DbSet — verifying EF Core can generate valid SQL.
    /// </summary>
    public static IEnumerable<Func<(string EntityName, Func<WireBoundDbContext, Task> QueryAction)>> GetDbSetQueryActions()
    {
        yield return () => ("Settings", async ctx => await ctx.Settings.FirstOrDefaultAsync());
        yield return () => ("HourlyUsages", async ctx => await ctx.HourlyUsages.FirstOrDefaultAsync());
        yield return () => ("DailyUsages", async ctx => await ctx.DailyUsages.FirstOrDefaultAsync());
        yield return () => ("SpeedSnapshots", async ctx => await ctx.SpeedSnapshots.FirstOrDefaultAsync());
        yield return () => ("AppUsageRecords", async ctx => await ctx.AppUsageRecords.FirstOrDefaultAsync());
        yield return () => ("HourlySystemStats", async ctx => await ctx.HourlySystemStats.FirstOrDefaultAsync());
        yield return () => ("DailySystemStats", async ctx => await ctx.DailySystemStats.FirstOrDefaultAsync());
        yield return () => ("AddressUsageRecords", async ctx => await ctx.AddressUsageRecords.FirstOrDefaultAsync());
        yield return () => ("ResourceInsightSnapshots", async ctx => await ctx.ResourceInsightSnapshots.FirstOrDefaultAsync());
        yield return () => ("AppCategoryMappings", async ctx => await ctx.AppCategoryMappings.FirstOrDefaultAsync());
    }

    /// <summary>
    /// Provides an insert action for each DbSet (except Settings which is seeded).
    /// </summary>
    public static IEnumerable<Func<(string EntityName, Func<WireBoundDbContext, Task> InsertAction)>> GetDbSetInsertActions()
    {
        yield return () => ("HourlyUsages", async ctx =>
        {
            ctx.HourlyUsages.Add(new() { Hour = DateTime.Now, AdapterId = "test" });
            await ctx.SaveChangesAsync();
        }
        );
        yield return () => ("DailyUsages", async ctx =>
        {
            ctx.DailyUsages.Add(new() { Date = DateOnly.FromDateTime(DateTime.Now), AdapterId = "test" });
            await ctx.SaveChangesAsync();
        }
        );
        yield return () => ("SpeedSnapshots", async ctx =>
        {
            ctx.SpeedSnapshots.Add(new() { Timestamp = DateTime.Now });
            await ctx.SaveChangesAsync();
        }
        );
        yield return () => ("AppUsageRecords", async ctx =>
        {
            ctx.AppUsageRecords.Add(new() { Timestamp = DateTime.Now, AppIdentifier = "test" });
            await ctx.SaveChangesAsync();
        }
        );
        yield return () => ("HourlySystemStats", async ctx =>
        {
            ctx.HourlySystemStats.Add(new() { Hour = DateTime.Now });
            await ctx.SaveChangesAsync();
        }
        );
        yield return () => ("DailySystemStats", async ctx =>
        {
            ctx.DailySystemStats.Add(new() { Date = DateOnly.FromDateTime(DateTime.Now) });
            await ctx.SaveChangesAsync();
        }
        );
        yield return () => ("AddressUsageRecords", async ctx =>
        {
            ctx.AddressUsageRecords.Add(new() { Timestamp = DateTime.Now, RemoteAddress = "127.0.0.1" });
            await ctx.SaveChangesAsync();
        }
        );
        yield return () => ("ResourceInsightSnapshots", async ctx =>
        {
            ctx.ResourceInsightSnapshots.Add(new() { Timestamp = DateTime.Now, AppIdentifier = "test" });
            await ctx.SaveChangesAsync();
        }
        );
        yield return () => ("AppCategoryMappings", async ctx =>
        {
            ctx.AppCategoryMappings.Add(new() { ExecutableName = "test.exe", CategoryName = "Test" });
            await ctx.SaveChangesAsync();
        }
        );
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a WireBoundDbContext backed by a database that was migrated from PK-only stubs.
    /// Simulates upgrading the oldest possible database to the current schema.
    /// </summary>
    private static async Task<WireBoundDbContext> CreateMigratedContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        // Discover all tables from a fresh EnsureCreated schema
        var allTables = await DiscoverTablesAsync();

        // Create PK-only stub tables (simulates the oldest possible database)
        foreach (var table in allTables)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"CREATE TABLE [{table}] (Id INTEGER PRIMARY KEY AUTOINCREMENT)";
            await cmd.ExecuteNonQueryAsync();
        }

        // Seed the Settings singleton row
        await using (var seedCmd = connection.CreateCommand())
        {
            seedCmd.CommandText = "INSERT INTO Settings (Id) VALUES (1)";
            await seedCmd.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<WireBoundDbContext>()
            .UseSqlite(connection)
            .Options;

        // Run migrations
        var context = new WireBoundDbContext(options);
        context.ApplyMigrations();

        // Reopen connection if ApplyMigrations closed it
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        return context;
    }

    /// <summary>
    /// Dynamically discovers all entity tables from EnsureCreated.
    /// Never hardcode table lists — this ensures new tables are automatically included.
    /// </summary>
    private static async Task<List<string>> DiscoverTablesAsync()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<WireBoundDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new WireBoundDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var tables = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));

        return tables;
    }

    /// <summary>
    /// Creates stub tables (PK only) for any tables not yet created,
    /// so ApplyMigrations can run without "no such table" errors.
    /// Dynamically discovers tables from EnsureCreated to stay in sync automatically.
    /// </summary>
    private static async Task EnsureAllTablesExist(SqliteConnection connection, string skipTable)
    {
        var allTables = await DiscoverTablesAsync();

        foreach (var table in allTables)
        {
            if (string.Equals(table, skipTable, StringComparison.OrdinalIgnoreCase))
                continue;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"CREATE TABLE IF NOT EXISTS [{table}] (Id INTEGER PRIMARY KEY AUTOINCREMENT)";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<Dictionary<string, List<string>>> GetSchemaAsync(SqliteConnection connection)
    {
        var schema = new Dictionary<string, List<string>>();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                schema[reader.GetString(0)] = [];
        }

        foreach (var tableName in schema.Keys)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info([{tableName}])";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                schema[tableName].Add(reader.GetString(1));
        }

        return schema;
    }

    private static async Task<List<string>> GetColumnsAsync(SqliteConnection connection, string tableName)
    {
        var columns = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{tableName}])";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
        return columns;
    }

    #endregion
}
