using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WireBound.Core.Data;

namespace WireBound.Tests.Fixtures;

/// <summary>
/// Base class for tests that require a real SQLite in-memory database.
/// Use this instead of <see cref="DatabaseTestBase"/> when tests need features
/// not supported by the EF Core InMemory provider (e.g., ExecuteDeleteAsync).
/// </summary>
public abstract class SqliteDatabaseTestBase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    protected readonly IServiceProvider ServiceProvider;

    protected SqliteDatabaseTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<WireBoundDbContext>(options =>
            options.UseSqlite(_connection));

        ConfigureServices(services);

        ServiceProvider = services.BuildServiceProvider();

        // Initialize the database schema
        using var scope = ServiceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
        context.Database.EnsureCreated();
    }

    /// <summary>
    /// Override to register additional services for testing.
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
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

        _connection.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
