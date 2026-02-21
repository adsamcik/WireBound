namespace WireBound.Tests.Fixtures;

/// <summary>
/// Skips the test when not running on Windows.
/// </summary>
public class WindowsOnlyAttribute() : SkipAttribute("Requires Windows")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context)
        => Task.FromResult(!OperatingSystem.IsWindows());
}

/// <summary>
/// Skips the test when not running on Linux.
/// </summary>
public class LinuxOnlyAttribute() : SkipAttribute("Requires Linux")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context)
        => Task.FromResult(!OperatingSystem.IsLinux());
}
