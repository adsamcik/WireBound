namespace WireBound.Avalonia.Services;

internal sealed record RancherToolPaths(string Rdctl, string Nerdctl, string Docker)
{
    internal static RancherToolPaths? Find()
    {
        var extension = OperatingSystem.IsWindows() ? ".exe" : "";
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rd", "bin"));
        if (OperatingSystem.IsWindows())
        {
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rancher Desktop", "resources", "resources", "win32", "bin"));
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Rancher Desktop", "resources", "resources", "win32", "bin"));
        }
        foreach (var directory in directories.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            try
            {
                var rdctl = new FileInfo(Path.Combine(directory.Trim('"'), "rdctl" + extension));
                if (!rdctl.Exists) continue;
                var path = (rdctl.ResolveLinkTarget(true) ?? rdctl).FullName;
                var parent = Path.GetDirectoryName(path)!;
                var nerdctl = Path.Combine(parent, "nerdctl" + extension);
                var docker = Path.Combine(parent, "docker" + extension);
                if (File.Exists(nerdctl) && File.Exists(docker)) return new(path, nerdctl, docker);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }
        return null;
    }
}

internal sealed record WorkloadCommand(string Executable, IReadOnlyList<string> Arguments, int TimeoutSeconds = 5,
    IReadOnlyDictionary<string, string?>? Environment = null);
