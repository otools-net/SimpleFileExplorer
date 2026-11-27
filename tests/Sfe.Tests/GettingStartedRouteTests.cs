using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>The getting-started checklist's route: each item ticks itself when what it asks for is done.</summary>
[TestClass]
public sealed class GettingStartedRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        Directory.CreateDirectory(_library.FullPath("Media"));
        _factory = ApiClients.CreateFactory(_library);
        _admin = await ApiClients.SetUpAsync(_factory, Token);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _admin.Dispose();
        _factory.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ANewServerHasOnlyItsLibraryFolderDone()
    {
        var items = await ItemsAsync(_admin);

        CollectionAssert.AreEqual(
            new[] { "library", "groups", "folders", "accounts", "connection" },
            items.Select(item => item.Id).ToArray(),
            "A server administrator sees every item, in order"
        );
        Assert.IsTrue(items[0].Done, "The library folder is there");
        Assert.IsFalse(items.Skip(1).Any(item => item.Done), "Nothing else is set up yet");
    }

    [TestMethod]
    public async Task EachItemTicksItselfWhenDone()
    {
        var group = await CreateGroupAsync("Family", [Rights.Browse]);
        await CreateAccountAsync("bob", group);
        using (
            var rule = await _admin.PutAsJsonAsync(
                $"{V1}/access/folder-rules",
                new FolderRuleChange("Media", group, null, FolderLevel.Read),
                Json,
                Token
            )
        )
        {
            Assert.AreEqual(HttpStatusCode.OK, rule.StatusCode);
        }
        await AddLibraryConnectionAsync();

        var items = await ItemsAsync(_admin);

        Assert.IsTrue(items.All(item => item.Done), string.Join(", ", items.Where(i => !i.Done).Select(i => i.Id)));
    }

    [TestMethod]
    public async Task AnAccountSeesOnlyTheItemsOfItsRights()
    {
        var group = await CreateGroupAsync("Helpers", [Rights.Browse, Rights.ManageMembers]);
        await CreateAccountAsync("helper", group);
        var plain = await CreateGroupAsync("Plain", [Rights.Browse]);
        await CreateAccountAsync("plain", plain);
        using var helper = await ApiClients.SignInAsync(_factory, "helper", "helper-password", Token);
        using var refused = await ApiClients.SignInAsync(_factory, "plain", "plain-password", Token);

        var items = await ItemsAsync(helper);
        using var none = await refused.GetAsync($"{V1}/access/getting-started", Token);

        CollectionAssert.AreEqual(
            new[] { "groups", "folders", "accounts" },
            items.Select(item => item.Id).ToArray(),
            "A space administrator sees neither the library folder nor connections"
        );
        Assert.IsTrue(items.Single(item => item.Id == "accounts").Done, "The other accounts of the space count");
        Assert.AreEqual(HttpStatusCode.Forbidden, none.StatusCode);
    }

    private async Task<IReadOnlyList<GettingStartedItem>> ItemsAsync(HttpClient client)
    {
        var view = await client.GetFromJsonAsync<GettingStartedView>($"{V1}/access/getting-started", Json, Token);
        return view!.Items;
    }

    private async Task<int> CreateGroupAsync(string name, IReadOnlyList<string> rights)
    {
        using var response = await _admin.PostAsJsonAsync(
            $"{V1}/access/groups",
            new GroupInput(name, Rights: rights),
            Json,
            Token
        );
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<GroupInfo>(Json, Token))!.Id;
    }

    private async Task CreateAccountAsync(string name, int groupId)
    {
        using var response = await _admin.PostAsJsonAsync(
            $"{V1}/access/users",
            new UserInput(name, Password: name + "-password", GroupIds: [groupId]),
            Json,
            Token
        );
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
    }

    private async Task AddLibraryConnectionAsync()
    {
        var dbFactory = _factory.Services.GetRequiredService<IDbContextFactory<AccessDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(Token);
        var tenant = await db.Tenants.SingleAsync(t => t.IsDefault, Token);
        db.Connections.Add(
            new Connection
            {
                Kind = FakeProvider.FakeKind,
                Name = "Photos",
                Level = ConnectionLevel.Library,
                TenantId = tenant.Id,
                Created = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync(Token);
    }
}
