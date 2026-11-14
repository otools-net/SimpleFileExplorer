using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Tests.Infrastructure;

/// <summary>
/// Programs calling a test server with a personal access token: no cookie, no <c>X-SFE-Request</c>, only
/// <c>Authorization: Bearer</c>; and the accounts and tokens they need.
/// </summary>
public static class TokenClients
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>A client sending <paramref name="token"/> and nothing else (from <paramref name="address"/>).</summary>
    public static HttpClient Bearer(WebApplicationFactory<Program> factory, string token, string? address = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (address is not null)
            client.DefaultRequestHeaders.Add(ApiClients.AddressHeader, address);
        return client;
    }

    /// <summary>A token of account <paramref name="userId"/>, made by the service itself.</summary>
    public static async Task<CreatedAccessToken> CreateAsync(
        WebApplicationFactory<Program> factory,
        int userId,
        AccessTokenDraft draft,
        CancellationToken cancellationToken
    )
    {
        var result = await factory
            .Services.GetRequiredService<AccessTokens>()
            .CreateAsync(userId, draft, cancellationToken);
        Assert.AreEqual(AccessStatus.Ok, result.Status, result.Problem);
        return result.Value!;
    }

    /// <summary>A local account made by <paramref name="admin"/>, its password its name and "-password".</summary>
    public static async Task<UserInfo> CreateUserAsync(
        HttpClient admin,
        string name,
        IReadOnlyList<int> groupIds,
        CancellationToken cancellationToken
    )
    {
        using var response = await admin.PostAsJsonAsync(
            $"{V1}/access/users",
            new UserInput(name, Password: $"{name}-password", GroupIds: groupIds),
            Json,
            cancellationToken
        );
        Assert.AreEqual(
            HttpStatusCode.Created,
            response.StatusCode,
            await response.Content.ReadAsStringAsync(cancellationToken)
        );
        return (await response.Content.ReadFromJsonAsync<UserInfo>(Json, cancellationToken))!;
    }

    /// <summary>The built-in group <paramref name="builtIn"/> of the space <paramref name="admin"/> is in.</summary>
    public static async Task<GroupInfo> GroupAsync(
        HttpClient admin,
        BuiltInGroup builtIn,
        CancellationToken cancellationToken,
        int? tenantId = null
    )
    {
        var url = tenantId is { } id ? $"{V1}/access/groups?tenant={id}" : $"{V1}/access/groups";
        var groups = await GetAsync<List<GroupInfo>>(admin, url, cancellationToken);
        return groups.Single(g => g.BuiltIn == builtIn);
    }

    /// <summary>A GET answering 200, read as <typeparamref name="T"/>.</summary>
    public static async Task<T> GetAsync<T>(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.OK,
            response.StatusCode,
            $"{url}: {await response.Content.ReadAsStringAsync(cancellationToken)}"
        );
        return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
    }
}
