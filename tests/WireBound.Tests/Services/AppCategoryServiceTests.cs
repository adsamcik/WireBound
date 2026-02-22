using WireBound.Avalonia.Services;
using WireBound.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WireBound.Tests.Services;

public class AppCategoryServiceTests
{
    private static AppCategoryService CreateService()
    {
        var options = new DbContextOptionsBuilder<WireBoundDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using (var ctx = new WireBoundDbContext(options))
            ctx.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddScoped(_ => new WireBoundDbContext(options));
        var serviceProvider = services.BuildServiceProvider();
        return new AppCategoryService(serviceProvider);
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

        var service = new AppCategoryService(serviceProvider);
        await service.LoadUserOverridesAsync();

        service.GetCategory("chrome").Should().Be("Gaming");
        // Other defaults should still work
        service.GetCategory("firefox").Should().Be("Web Browsers");
    }
}
