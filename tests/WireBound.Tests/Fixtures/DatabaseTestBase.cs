using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WireBound.Core.Data;

namespace WireBound.Tests.Fixtures;

/// <summary>
/// Base class for tests that require an in-memory database.
/// Provides a fresh database for each test to ensure isolation.
/// </summary>
public abstract class DatabaseTestBase : IAsyncDisposable
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly string DatabaseName;

    protected DatabaseTestBase()
    {
        DatabaseName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddDbContext<WireBoundDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: DatabaseName));

        ConfigureServices(services);

        ServiceProvider = services.BuildServiceProvider();

        // Initialize the database
        using var scope = ServiceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
        context.Database.EnsureCreated();
    }

    /// <summary>
    /// Override to register additional services for testing.
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // Base implementation does nothing
    }

    /// <summary>
    /// Gets a fresh database context for assertions.
    /// Remember to dispose the scope when done.
    /// </summary>
    protected WireBoundDbContext GetContext()
    {
        var scope = ServiceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
    }

    /// <summary>
    /// Creates a scoped database context for operations that need explicit scope management.
    /// </summary>
    protected IServiceScope CreateScope()
    {
        return ServiceProvider.CreateScope();
    }

    public virtual ValueTask DisposeAsync()
    {
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
