using WireBound.Platform.Abstract.Helpers;

namespace WireBound.Tests.Helpers;

public class AppDataPathsTests : IAsyncDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"wirebound-test-{Guid.NewGuid():N}");

    public AppDataPathsTests() => Directory.CreateDirectory(_testDirectory);

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
        catch
        {
            // Best effort cleanup for a temporary test directory.
        }

        return ValueTask.CompletedTask;
    }

    [Test]
    public void MigrateLegacyPersistentData_CopiesKnownDataAndPreservesLegacyInstall()
    {
        var legacyDirectory = Path.Combine(_testDirectory, "WireBound");
        var dataDirectory = Path.Combine(_testDirectory, "WireBoundData");
        Directory.CreateDirectory(Path.Combine(legacyDirectory, "current"));
        Directory.CreateDirectory(Path.Combine(legacyDirectory, "logs"));
        Directory.CreateDirectory(Path.Combine(legacyDirectory, "app-icons"));

        File.WriteAllText(Path.Combine(legacyDirectory, "wirebound.db"), "database");
        File.WriteAllText(Path.Combine(legacyDirectory, "wirebound.db-wal"), "write-ahead log");
        File.WriteAllText(Path.Combine(legacyDirectory, ".elevation-secret"), "secret");
        File.WriteAllText(Path.Combine(legacyDirectory, "logs", "wirebound.log"), "log");
        File.WriteAllText(Path.Combine(legacyDirectory, "app-icons", "app.png"), "icon");
        File.WriteAllText(Path.Combine(legacyDirectory, "Update.exe"), "installer sentinel");
        File.WriteAllText(Path.Combine(legacyDirectory, "current", "WireBound.exe"), "app sentinel");

        AppDataPaths.MigrateLegacyPersistentData(legacyDirectory, dataDirectory);

        File.ReadAllText(Path.Combine(dataDirectory, "wirebound.db")).Should().Be("database");
        File.ReadAllText(Path.Combine(dataDirectory, "wirebound.db-wal")).Should().Be("write-ahead log");
        File.ReadAllText(Path.Combine(dataDirectory, ".elevation-secret")).Should().Be("secret");
        File.ReadAllText(Path.Combine(dataDirectory, "logs", "wirebound.log")).Should().Be("log");
        File.ReadAllText(Path.Combine(dataDirectory, "app-icons", "app.png")).Should().Be("icon");

        File.Exists(Path.Combine(legacyDirectory, "Update.exe")).Should().BeTrue();
        File.Exists(Path.Combine(legacyDirectory, "current", "WireBound.exe")).Should().BeTrue();
        File.ReadAllText(Path.Combine(legacyDirectory, "wirebound.db")).Should().Be("database");
    }

    [Test]
    public void MigrateLegacyPersistentData_DoesNotOverwriteExistingData()
    {
        var legacyDirectory = Path.Combine(_testDirectory, "WireBound");
        var dataDirectory = Path.Combine(_testDirectory, "WireBoundData");
        Directory.CreateDirectory(legacyDirectory);
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Combine(legacyDirectory, "wirebound.db"), "legacy database");
        File.WriteAllText(Path.Combine(dataDirectory, "wirebound.db"), "current database");

        AppDataPaths.MigrateLegacyPersistentData(legacyDirectory, dataDirectory);

        File.ReadAllText(Path.Combine(dataDirectory, "wirebound.db")).Should().Be("current database");
        Directory.GetDirectories(_testDirectory, "*.migrating-*").Should().BeEmpty();
    }
}
