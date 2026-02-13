using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WireBound.Core.Data;

namespace WireBound.Tests.Data;

/// <summary>
/// Verifies that ApplyMigrations() handles all model columns,
/// so that any pre-existing database can be fully migrated.
/// If a new property is added to a model without a corresponding
/// migration in ApplyMigrations(), these tests will fail.
/// </summary>
public class MigrationCompletenessTests
{
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

    /// <summary>
    /// Provides test data: each entity table with its expected columns from a fresh EnsureCreated schema.
    /// </summary>
    public static async Task<List<(string TableName, List<string> ExpectedColumns)>> GetEntityTables()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<WireBoundDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new WireBoundDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var schema = await GetSchemaAsync(connection);
        return schema.Select(kvp => (kvp.Key, kvp.Value)).ToList();
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

    /// <summary>
    /// Creates stub tables (PK only) for any tables not yet created,
    /// so ApplyMigrations can run without "no such table" errors.
    /// </summary>
    private static async Task EnsureAllTablesExist(SqliteConnection connection, string skipTable)
    {
        var allTables = new[]
        {
            "Settings", "HourlyUsages", "DailyUsages", "SpeedSnapshots",
            "AppUsageRecords", "HourlySystemStats", "DailySystemStats", "AddressUsageRecords"
        };

        foreach (var table in allTables)
        {
            if (string.Equals(table, skipTable, StringComparison.OrdinalIgnoreCase))
                continue;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"CREATE TABLE IF NOT EXISTS [{table}] (Id INTEGER PRIMARY KEY AUTOINCREMENT)";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
