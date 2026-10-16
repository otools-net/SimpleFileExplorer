using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Api.Maintenance;
using Sfe.Api.Security;
using Sfe.Core.Access;

namespace Sfe.Tests.Infrastructure;

/// <summary>HTTP clients of a test server: they keep the sign-in cookie and send the header requests that change something need.</summary>
public static class ApiClients
{
    public const string AdminName = "admin";
    public const string AdminPassword = "admin-password";

    /// <summary>A server on <paramref name="library"/>, with changes to its configuration.</summary>
    public static WebApplicationFactory<Program> CreateFactory(TestLibrary library, Action<IDictionary<string, string?>>? configure = null)
    {
        var settings = library.Settings();
        configure?.Invoke(settings);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, TestAddressFilter>());
        });
    }

    /// <summary>
    /// The test server has no network: a request with this header comes from that address (before the app's own
    /// middleware, as a connection would), e.g. to test the limits per address or a trusted proxy.
    /// </summary>
    public const string AddressHeader = "X-Test-Remote-Address";

    /// <summary>A client of <paramref name="factory"/> whose requests come from <paramref name="address"/>.</summary>
    public static HttpClient CreateFrom(WebApplicationFactory<Program> factory, string address)
    {
        var client = Create(factory);
        client.DefaultRequestHeaders.Add(AddressHeader, address);
        return client;
    }

    private sealed class TestAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers[AddressHeader].ToString() is { Length: > 0 } address)
                    context.Connection.RemoteIpAddress = IPAddress.Parse(address);
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    /// <summary>A client that is not signed in (a visitor without an account).</summary>
    public static HttpClient Create(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add(RequestGuard.Header, "1");
        return client;
    }

    /// <summary>Runs the setup with <see cref="TestLibrary.SetupCode"/>: the client is signed in as the server administrator.</summary>
    public static async Task<HttpClient> SetUpAsync(WebApplicationFactory<Program> factory, CancellationToken cancellationToken)
    {
        var client = Create(factory);
        using var response = await client.PostAsJsonAsync("/api/setup", new SetupInput(TestLibrary.SetupCode, AdminName, AdminPassword), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        return client;
    }

    /// <summary>Confirms the update of an install of an earlier version with <see cref="TestLibrary.SetupCode"/> and waits until it is done.</summary>
    public static async Task MigrateAsync(WebApplicationFactory<Program> factory, CancellationToken cancellationToken)
    {
        var client = Create(factory);
        using (var response = await client.PostAsJsonAsync("/api/migration", new MigrationInput(TestLibrary.SetupCode), cancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));

        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
        var timeout = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var info = (await client.GetFromJsonAsync<MigrationInfo>("/api/migration", json, cancellationToken))!;
            if (info.Status == MigrationStatus.None)
                return;
            Assert.AreNotEqual(MigrationStatus.Failed, info.Status, info.Error);
            Assert.IsLessThan(timeout, DateTime.UtcNow, "The update takes too long");
            await Task.Delay(50, cancellationToken);
        }
    }

    /// <summary>A new client signed in with a name and password.</summary>
    public static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string userName, string password, CancellationToken cancellationToken)
    {
        var client = Create(factory);
        using var response = await client.PostAsJsonAsync("/api/session/login", new SignInRequest(userName, password), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        return client;
    }
}
