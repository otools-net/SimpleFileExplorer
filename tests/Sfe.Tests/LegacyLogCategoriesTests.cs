using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core;
using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Core.Logs;
using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Log levels written for the categories of before the namespaces became <c>Otools.Net.Sfe.*</c> (<c>Sfe…</c>) still
/// apply to the renamed categories (<see cref="LegacyLogCategories"/>).
/// </summary>
[TestClass]
public sealed class LegacyLogCategoriesTests
{
    [TestMethod]
    public void AnOldCategoryKeySetsTheLevelOfTheRenamedOnes()
    {
        using var services = Services(
            ("Logging:LogLevel:Default", "Warning"),
            ("Logging:LogLevel:Sfe.Core.Indexing", "Debug")
        );
        var loggers = services.GetRequiredService<ILoggerFactory>();

        Assert.AreEqual("Otools.Net.Sfe.Core.Indexing.DirectoryIndexer", typeof(DirectoryIndexer).FullName);
        Assert.IsTrue(services.GetRequiredService<ILogger<DirectoryIndexer>>().IsEnabled(LogLevel.Debug));
        Assert.IsFalse(services.GetRequiredService<ILogger<TagService>>().IsEnabled(LogLevel.Information));
        Assert.IsTrue(loggers.CreateLogger("Sfe.Core.Indexing.Probe").IsEnabled(LogLevel.Debug));
    }

    [TestMethod]
    public void AKeyNamingTheNewCategoryWins()
    {
        using var services = Services(("Logging:LogLevel:Sfe", "Debug"), ("Logging:LogLevel:Otools.Net.Sfe", "Error"));

        Assert.IsFalse(services.GetRequiredService<ILogger<DirectoryIndexer>>().IsEnabled(LogLevel.Warning));
        Assert.IsTrue(
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Sfe.Api.Problems").IsEnabled(LogLevel.Debug)
        );
    }

    [TestMethod]
    public void AnOldKeyOfAProviderSectionAppliesToThatProvider()
    {
        using var services = Services(
            ("Logging:LogLevel:Default", "Warning"),
            ($"Logging:{typeof(ProbeProvider).FullName}:LogLevel:Sfe", "Trace")
        );

        Assert.IsTrue(services.GetRequiredService<ILogger<DirectoryIndexer>>().IsEnabled(LogLevel.Trace));
    }

    /// <summary>
    /// The server's container (<see cref="AccessServiceCollectionExtensions.AddSfeAccess"/>) with these settings and a
    /// provider that takes every level, so that <see cref="ILogger.IsEnabled"/> tells what the filters let through.
    /// </summary>
    private static ServiceProvider Services(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            // Required by AddSfeAccess, never opened here.
            .AddInMemoryCollection([new("ConnectionStrings:Sfe", "Data Source=:memory:")])
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
            logging.AddConfiguration(configuration.GetSection("Logging")).AddProvider(new ProbeProvider())
        );
        services.AddSfeAccess(configuration, Path.GetTempPath());
        return services.BuildServiceProvider();
    }

    private sealed class ProbeProvider : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) { }

        public void Dispose() { }
    }
}
