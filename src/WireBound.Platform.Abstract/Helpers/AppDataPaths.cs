namespace WireBound.Platform.Abstract.Helpers;

/// <summary>
/// Provides locations for persistent WireBound user data.
/// </summary>
/// <remarks>
/// The Velopack Windows installer uses <c>%LocalAppData%\WireBound</c> as
/// its installation directory. Persistent data must never be stored there:
/// repair and uninstall operations replace or remove that directory.
/// </remarks>
public static class AppDataPaths
{
    private const string DataDirectoryName = "WireBoundData";
    private const string LegacyDataDirectoryName = "WireBound";

    /// <summary>Gets the persistent per-user WireBound data directory.</summary>
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        DataDirectoryName);

    /// <summary>Gets the legacy data directory that collides with Velopack's Windows install location.</summary>
    public static string LegacyDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        LegacyDataDirectoryName);

    /// <summary>Builds a path beneath the persistent per-user data directory.</summary>
    public static string GetPath(params string[] paths) => Path.Combine([DataDirectory, .. paths]);

    /// <summary>
    /// Copies WireBound's known persistent files out of its legacy directory.
    /// The method is idempotent, stages a complete copy before activating it,
    /// and intentionally leaves Velopack's installation files (such as
    /// <c>current</c> and <c>Update.exe</c>) untouched.
    /// </summary>
    public static void MigrateLegacyPersistentData()
    {
        try
        {
            MigrateLegacyPersistentData(LegacyDataDirectory, DataDirectory);
        }
        catch
        {
            // Migration is best effort. The old location remains intact and the
            // next normal application start can retry once transient file locks clear.
        }
    }

    internal static void MigrateLegacyPersistentData(string legacyDataDirectory, string dataDirectory)
    {
        if (!Directory.Exists(legacyDataDirectory))
        {
            return;
        }

        var parentDirectory = Path.GetDirectoryName(dataDirectory)
            ?? throw new InvalidOperationException("The data directory must have a parent directory.");
        Directory.CreateDirectory(parentDirectory);

        var stagingDirectory = $"{dataDirectory}.migrating-{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            CopyLegacyFile(legacyDataDirectory, stagingDirectory, "wirebound.db");
            CopyLegacyFile(legacyDataDirectory, stagingDirectory, "wirebound.db-shm");
            CopyLegacyFile(legacyDataDirectory, stagingDirectory, "wirebound.db-wal");
            CopyLegacyFile(legacyDataDirectory, stagingDirectory, ".elevation-secret");
            CopyLegacyDirectory(legacyDataDirectory, stagingDirectory, "logs");
            CopyLegacyDirectory(legacyDataDirectory, stagingDirectory, "app-icons");

            if (!Directory.Exists(dataDirectory))
            {
                Directory.Move(stagingDirectory, dataDirectory);
                return;
            }

            CopyDirectory(stagingDirectory, dataDirectory);
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
            }
            catch
            {
                // A leftover staging directory is harmless and can be removed later.
            }
        }
    }

    private static void CopyLegacyFile(string legacyDataDirectory, string dataDirectory, string fileName)
    {
        var source = Path.Combine(legacyDataDirectory, fileName);
        var destination = Path.Combine(dataDirectory, fileName);

        if (File.Exists(source))
        {
            File.Copy(source, destination, overwrite: false);
        }
    }

    private static void CopyLegacyDirectory(string legacyDataDirectory, string dataDirectory, string directoryName)
    {
        var source = Path.Combine(legacyDataDirectory, directoryName);
        var destination = Path.Combine(dataDirectory, directoryName);

        if (Directory.Exists(source))
        {
            CopyDirectory(source, destination);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var sourceFile in Directory.EnumerateFiles(source))
        {
            var destinationFile = Path.Combine(destination, Path.GetFileName(sourceFile));
            if (!File.Exists(destinationFile))
            {
                File.Copy(sourceFile, destinationFile, overwrite: false);
            }
        }

        foreach (var sourceDirectory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(
                sourceDirectory,
                Path.Combine(destination, Path.GetFileName(sourceDirectory)));
        }
    }
}
