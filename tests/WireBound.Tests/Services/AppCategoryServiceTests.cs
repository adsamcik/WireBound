using WireBound.Avalonia.Services;
using WireBound.Core.Data;
using WireBound.Platform.Abstract.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace WireBound.Tests.Services;

public class AppCategoryServiceTests
{
    private static AppCategoryService CreateService(
        IAppMetadataProvider? metadataProvider = null,
        IGameDetectionProvider? gameDetectionProvider = null)
    {
        var options = new DbContextOptionsBuilder<WireBoundDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using (var ctx = new WireBoundDbContext(options))
            ctx.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddScoped(_ => new WireBoundDbContext(options));
        var serviceProvider = services.BuildServiceProvider();
        return new AppCategoryService(
            serviceProvider,
            metadataProvider ?? Substitute.For<IAppMetadataProvider>(),
            gameDetectionProvider ?? Substitute.For<IGameDetectionProvider>());
    }

    [Test]
    public void GetCategory_KnownBrowser_ReturnsWebBrowsers()
    {
        var service = CreateService();

        service.GetCategory("chrome").Should().Be("Web Browsers");
        service.GetCategory("firefox").Should().Be("Web Browsers");
        service.GetCategory("msedge").Should().Be("Web Browsers");
    }

    [Test]
    public void GetCategory_KnownDevTool_ReturnsDevelopmentTools()
    {
        var service = CreateService();

        service.GetCategory("code").Should().Be("Development Tools");
        service.GetCategory("rider").Should().Be("Development Tools");
        service.GetCategory("dotnet").Should().Be("Development Tools");
    }

    [Test]
    public void GetCategory_SystemProcess_ReturnsSystemServices()
    {
        var service = CreateService();

        service.GetCategory("svchost").Should().Be("System Services");
        service.GetCategory("explorer").Should().Be("System Services");
    }

    [Test]
    public void GetCategory_UnknownExe_ReturnsOther()
    {
        var service = CreateService();

        service.GetCategory("myunknownapp").Should().Be("Other");
    }

    [Test]
    public void GetCategory_WithExtension_StripsAndMatches()
    {
        var service = CreateService();

        service.GetCategory("chrome.exe").Should().Be("Web Browsers");
        service.GetCategory("code.exe").Should().Be("Development Tools");
    }

    [Test]
    public void GetCategory_CaseInsensitive()
    {
        var service = CreateService();

        service.GetCategory("CHROME").Should().Be("Web Browsers");
        service.GetCategory("Chrome").Should().Be("Web Browsers");
    }

    [Test]
    public void GetCategory_EmptyOrNull_ReturnsOther()
    {
        var service = CreateService();

        service.GetCategory("").Should().Be("Other");
        service.GetCategory(null!).Should().Be("Other");
    }

    [Test]
    public void GetAllCategories_ReturnsAllExpectedCategories()
    {
        var service = CreateService();

        var categories = service.GetAllCategories();

        categories.Should().Contain("Web Browsers");
        categories.Should().Contain("Development Tools");
        categories.Should().Contain("Communication");
        categories.Should().Contain("Media");
        categories.Should().Contain("Gaming");
        categories.Should().Contain("System Services");
        categories.Should().Contain("Office");
        categories.Should().Contain("Other");
    }

    [Test]
    public async Task LoadUserOverridesAsync_OverridesBuiltInMapping()
    {
        var options = new DbContextOptionsBuilder<WireBoundDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        // Seed user override: chrome → "Gaming" (overriding "Web Browsers")
        await using (var ctx = new WireBoundDbContext(options))
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.AppCategoryMappings.Add(new Core.Models.AppCategoryMapping
            {
                ExecutableName = "chrome",
                CategoryName = "Gaming",
                IsUserDefined = true
            });
            await ctx.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => new WireBoundDbContext(options));
        var serviceProvider = services.BuildServiceProvider();

        var service = new AppCategoryService(
            serviceProvider,
            Substitute.For<IAppMetadataProvider>(),
            Substitute.For<IGameDetectionProvider>());
        await service.LoadUserOverridesAsync();

        service.GetCategory("chrome").Should().Be("Gaming");
        // Other defaults should still work
        service.GetCategory("firefox").Should().Be("Web Browsers");
    }

    // ── Pipeline tests (new layered detection) ──────────────────────────

    [Test]
    public void GetCategory_WithPath_PublisherMapping_CategorizesUnknownExe()
    {
        var metadata = Substitute.For<IAppMetadataProvider>();
        metadata.GetPublisher(@"C:\Program Files\JetBrains\PhpStorm\bin\phpstorm64.exe")
            .Returns("JetBrains s.r.o.");
        var service = CreateService(metadata);

        var result = service.GetCategory("phpstorm64", @"C:\Program Files\JetBrains\PhpStorm\bin\phpstorm64.exe");

        result.Should().Be("Development Tools");
    }

    [Test]
    public void GetCategory_WithPath_PublisherMapping_ValveIsGaming()
    {
        var metadata = Substitute.For<IAppMetadataProvider>();
        metadata.GetPublisher(@"C:\Games\SomeGame\game.exe")
            .Returns("Valve Corporation");
        var service = CreateService(metadata);

        var result = service.GetCategory("game", @"C:\Games\SomeGame\game.exe");

        result.Should().Be("Gaming");
    }

    [Test]
    public void GetCategory_WithPath_ExeNameTakesPriorityOverPublisher()
    {
        var metadata = Substitute.For<IAppMetadataProvider>();
        // Chrome is a known exe → Web Browsers, even though Google could map to something else
        metadata.GetPublisher(Arg.Any<string>()).Returns("Google LLC");
        var service = CreateService(metadata);

        var result = service.GetCategory("chrome", @"C:\Program Files\Google\Chrome\chrome.exe");

        result.Should().Be("Web Browsers");
    }

    [Test]
    public void GetCategory_WithPath_OsMetadata_LinuxDesktopCategory()
    {
        var metadata = Substitute.For<IAppMetadataProvider>();
        metadata.GetPublisher(Arg.Any<string>()).Returns((string?)null);
        metadata.GetCategoryFromOsMetadata("krita").Returns("Media");
        var service = CreateService(metadata);

        var result = service.GetCategory("krita", "/usr/bin/krita");

        result.Should().Be("Media");
    }

    [Test]
    public void GetCategory_WithPath_PathHeuristic_SteamAppsIsGaming()
    {
        var metadata = Substitute.For<IAppMetadataProvider>();
        var service = CreateService(metadata);

        var result = service.GetCategory(
            "unknowngame",
            @"C:\Program Files (x86)\Steam\steamapps\common\SomeGame\unknowngame.exe");

        result.Should().Be("Gaming");
    }

    [Test]
    public void GetCategory_WithPath_PathHeuristic_System32IsSystem()
    {
        var metadata = Substitute.For<IAppMetadataProvider>();
        var service = CreateService(metadata);

        var result = service.GetCategory(
            "unknownsvc",
            @"C:\Windows\System32\unknownsvc.exe");

        result.Should().Be("System Services");
    }

    [Test]
    public void GetCategory_WithPath_ParentProcess_ChildOfSteamIsGaming()
    {
        var metadata = Substitute.For<IAppMetadataProvider>();
        metadata.GetParentProcessName(1234).Returns("steam");
        var service = CreateService(metadata);

        var result = service.GetCategory("unknowngame", null, processId: 1234);

        result.Should().Be("Gaming");
    }

    [Test]
    public void GetCategory_WithPath_ParentProcess_SkipsTransparentParents()
    {
        var metadata = Substitute.For<IAppMetadataProvider>();
        // Parent is explorer (transparent) — should NOT inherit System Services
        metadata.GetParentProcessName(1234).Returns("explorer");
        var service = CreateService(metadata);

        var result = service.GetCategory("unknownapp", null, processId: 1234);

        result.Should().Be("Other");
    }

    [Test]
    public void GetCategory_WithPath_PipelineCachesResults()
    {
        var metadata = Substitute.For<IAppMetadataProvider>();
        metadata.GetPublisher(@"C:\test\app.exe").Returns("JetBrains s.r.o.");
        var service = CreateService(metadata);

        // First call triggers publisher lookup
        service.GetCategory("app", @"C:\test\app.exe").Should().Be("Development Tools");

        // Second call should use cache (publisher not called again)
        service.GetCategory("app", @"C:\test\app.exe").Should().Be("Development Tools");
        metadata.Received(1).GetPublisher(@"C:\test\app.exe");
    }

    [Test]
    public void GetCategory_WithPath_FallsBackToOtherWhenNothingMatches()
    {
        var metadata = Substitute.For<IAppMetadataProvider>();
        var service = CreateService(metadata);

        var result = service.GetCategory(
            "totallyunknown",
            @"C:\Random\totallyunknown.exe",
            processId: 9999);

        result.Should().Be("Other");
    }

    [Test]
    public void GetCategory_WithPath_PipelinePriority_PublisherBeforePathHeuristic()
    {
        var metadata = Substitute.For<IAppMetadataProvider>();
        // Exe is in Steam folder but publisher says it's a dev tool
        metadata.GetPublisher(@"C:\Steam\steamapps\common\devtool\app.exe")
            .Returns("JetBrains s.r.o.");
        var service = CreateService(metadata);

        var result = service.GetCategory(
            "app",
            @"C:\Steam\steamapps\common\devtool\app.exe");

        // Publisher (Layer 2) should win over path heuristic (Layer 5)
        result.Should().Be("Development Tools");
    }
}
