using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Sfe.Api.Endpoints;
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
        });
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

    /// <summary>A new client signed in with a name and password.</summary>
    public static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string userName, string password, CancellationToken cancellationToken)
    {
        var client = Create(factory);
        using var response = await client.PostAsJsonAsync("/api/session/login", new SignInRequest(userName, password), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        return client;
    }
}
