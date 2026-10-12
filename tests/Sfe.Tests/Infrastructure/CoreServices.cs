using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sfe.Core;
using Sfe.Core.Data;

namespace Sfe.Tests.Infrastructure;

/// <summary>Core services wired against a <see cref="TestLibrary"/> and its SQLite index.</summary>
public sealed class CoreServices : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private CoreServices(ServiceProvider provider) => _provider = provider;

    public static async Task<CoreServices> CreateAsync(TestLibrary library, Action<IDictionary<string, string?>>? configure = null,
        Action<ILoggingBuilder>? logging = null)
    {
        var settings = library.Settings();
        configure?.Invoke(settings);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection()
            .AddLogging(builder => logging?.Invoke(builder))
            .AddSfeCore(configuration)
            .BuildServiceProvider(validateScopes: true);

        await services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        return new CoreServices(services);
    }

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
