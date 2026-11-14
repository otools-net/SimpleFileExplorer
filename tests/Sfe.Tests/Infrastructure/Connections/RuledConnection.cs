using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Data;

namespace Sfe.Tests.Infrastructure;

/// <summary>
/// A library-level connection with folder rules, for the attack matrix of the connections' routes: bob uses it
/// through his group Writers (row Write), whose rules hide <c>Private</c>, <c>Mixed/Hidden</c> and <c>Lead</c>
/// (but for <c>Lead/Open</c>, which <c>Lead</c> only leads to) and only let him read <c>ReadOnly</c>. The fake
/// provider's account records every call (<see cref="FakeAccount.Calls"/>, emptied once set up).
/// </summary>
public sealed class RuledConnection : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>What Writers grant: using connections and changing files, nothing of the library's settings.</summary>
    public static readonly string[] WriterRights =
    [
        Rights.UseConnections,
        Rights.Browse,
        Rights.ViewFiles,
        Rights.Download,
        Rights.CreateFolders,
        Rights.RenameFiles,
        Rights.DeleteFiles,
        Rights.MoveFiles,
        Rights.CopyFiles,
        Rights.UploadFiles,
    ];

    private readonly TestLibrary _library = new();

    private RuledConnection(WebApplicationFactory<Program> factory) => Factory = factory;

    public FakeProvider Provider { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; }
    public FakeAccount Account { get; private set; } = null!;
    public HttpClient Bob { get; private set; } = null!;
    public int TenantId { get; private set; }
    public int WritersId { get; private set; }
    public int BobId { get; private set; }
    public int Id { get; private set; }

    public TestLibrary Library => _library;

    /// <summary>The connection's routes, <c>/api/connections/{id}</c>.</summary>
    public string Base => $"/api/connections/{Id}";

    public static async Task<RuledConnection> CreateAsync(CancellationToken cancellationToken)
    {
        var world = new RuledConnection(null!);
        world.Factory = ApiClients
            .CreateFactory(world._library)
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IConnectionProvider>(world.Provider))
            );
        await world.SetUpAsync(cancellationToken);
        return world;
    }

    public T Get<T>()
        where T : notnull => Factory.Services.GetRequiredService<T>();

    /// <summary>The rules of the set-up (for Writers), with <paramref name="more"/>; replaces those there.</summary>
    public async Task SetRulesAsync(CancellationToken cancellationToken, params ConnectionRuleInput[] more)
    {
        ConnectionRuleInput[] rules =
        [
            Rule("Private", FolderLevel.Hidden),
            Rule("Mixed/Hidden", FolderLevel.Hidden),
            Rule("ReadOnly", FolderLevel.Read),
            Rule("Lead", FolderLevel.Hidden),
            Rule("Lead/Open", FolderLevel.Write),
            .. more,
        ];
        var set = await Get<ConnectionRuleService>().SetAsync(Id, TenantId, rules, null, cancellationToken);
        Assert.AreEqual(AccessStatus.Ok, set.Status, string.Join(", ", set.Errors?.Keys.ToArray() ?? []));
    }

    /// <summary>A rule for <paramref name="groupId"/> (Writers when null).</summary>
    public ConnectionRuleInput Rule(string path, FolderLevel level, int? groupId = null) =>
        new(path, groupId ?? WritersId, null, level, null);

    /// <summary>A group of the library with bob in it and <paramref name="rights"/>.</summary>
    public async Task<int> GroupAsync(string name, string[] rights, CancellationToken cancellationToken)
    {
        var input = new GroupInput(name, Rights: rights, MemberIds: [BobId]);
        var made = await Get<AccessService>().CreateGroupAsync(TenantId, input, cancellationToken: cancellationToken);
        return made.Value!.Id;
    }

    /// <summary>Which groups use the connection, and how far.</summary>
    public async Task ShareAsync(Dictionary<int, FolderLevel> groups, CancellationToken cancellationToken)
    {
        var shared = await Get<ConnectionSharing>().SetGroupsAsync(Id, TenantId, groups, null, cancellationToken);
        Assert.AreEqual(AccessStatus.Ok, shared.Status, shared.Problem);
    }

    /// <summary>The provider was called about <paramref name="part"/> (ignoring case).</summary>
    public bool Called(string part) => Account.Calls.Any(c => c.Contains(part, StringComparison.OrdinalIgnoreCase));

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    private async Task SetUpAsync(CancellationToken cancellationToken)
    {
        await ApiClients.SetUpAsync(Factory, cancellationToken);
        var accounts = Get<AccessService>();
        TenantId = (await accounts.GetTenantsAsync(cancellationToken)).Single().Id;
        var users = (await accounts.GetBuiltInGroupAsync(TenantId, BuiltInGroup.Users, cancellationToken))!.Id;
        var bob = new UserInput("bob", Password: "bob-password-1", GroupIds: [users]);
        BobId = (await accounts.CreateUserAsync(bob, null, cancellationToken)).Value!.Id;
        WritersId = await GroupAsync("Writers", WriterRights, cancellationToken);
        Bob = await ApiClients.SignInAsync(Factory, "bob", "bob-password-1", cancellationToken);

        Account = Provider
            .Account("acct")
            .WithFile("Public/a.txt", "public a")
            .WithFile("Public/b.jpg", "public b")
            .WithFile("Private/secret.txt", "secret")
            .WithFile("Private/Inner/deep.txt", "deep")
            .WithFile("Mixed/ok.txt", "ok")
            .WithFile("Mixed/Hidden/h.txt", "hidden")
            .WithFile("ReadOnly/r.txt", "read only")
            .WithFile("Lead/l.txt", "lead")
            .WithFile("Lead/Open/o.txt", "open");
        var draft = new ConnectionDraft(FakeProvider.FakeKind, "Shared", ConnectionLevel.Library, TenantId: TenantId);
        var grant = new ConnectionGrant("acct", "acct@fake", "id:acct");
        Id = (await Get<ConnectionService>().CreateAsync(draft, grant, null, cancellationToken)).Value!.Id;
        await ShareAsync(new() { [WritersId] = FolderLevel.Write }, cancellationToken);
        await SetRulesAsync(cancellationToken);
        Account.Calls.Clear();
    }
}
