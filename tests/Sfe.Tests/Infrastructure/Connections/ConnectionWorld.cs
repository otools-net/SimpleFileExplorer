using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Core;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Data;

namespace Sfe.Tests.Infrastructure;

/// <summary>
/// The server's services of connections (<c>AddSfeAccess</c> with the <see cref="FakeProvider"/> and the
/// <see cref="FakeProtector"/>) over a library's database, with two libraries and their people: alice in the Users
/// group of the first, bob in a "Team" group of it, carol in the second, a server administrator.
/// </summary>
public sealed class ConnectionWorld : IAsyncDisposable
{
    private ConnectionWorld(ServiceProvider services, FakeProvider provider)
    {
        Services = services;
        Provider = provider;
    }

    public ServiceProvider Services { get; }
    /// <summary>The fake kind (the server knows Dropbox too).</summary>
    public FakeProvider Provider { get; }
    public ConnectionService Connections => Services.GetRequiredService<ConnectionService>();
    public ConnectionSharing Sharing => Services.GetRequiredService<ConnectionSharing>();
    public ConnectionAccess Access => Services.GetRequiredService<ConnectionAccess>();

    public int Tenant { get; private set; }
    public int OtherTenant { get; private set; }
    public int Users { get; private set; }
    public int Team { get; private set; }
    public int OtherUsers { get; private set; }
    public int Alice { get; private set; }
    public int Bob { get; private set; }
    public int Carol { get; private set; }
    public int Admin { get; private set; }

    /// <param name="settings">Over the library's configuration (<c>Sfe:Connections:…</c>).</param>
    public static async Task<ConnectionWorld> CreateAsync(
        TestLibrary library,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? settings = null
    )
    {
        var provider = new FakeProvider();
        var world = new ConnectionWorld(Build(library, provider, settings), provider);
        await world.Services.GetRequiredService<AccessDatabaseInitializer>().InitializeAsync(cancellationToken);
        await world.PopulateAsync(library, cancellationToken);
        return world;
    }

    /// <summary>The same database and people with other settings, the fake provider's accounts kept.</summary>
    public ConnectionWorld WithSettings(TestLibrary library, IReadOnlyDictionary<string, string?> settings)
    {
        var other = new ConnectionWorld(Build(library, Provider, settings), Provider);
        other.CopyPeople(this);
        return other;
    }

    public AccessDbContext Db() => Services.GetRequiredService<IDbContextFactory<AccessDbContext>>().CreateDbContext();

    /// <summary>A connection made by signing in to the fake account <paramref name="account"/>.</summary>
    public async Task<ConnectionInfo> CreateAsync(
        ConnectionDraft draft,
        string account,
        CancellationToken cancellationToken
    )
    {
        var grant = await Provider.CompleteSignInAsync(
            new(
                SignInKind.OAuthCode,
                new(),
                "verifier-x",
                null,
                new Dictionary<string, string> { ["code"] = $"good:{account}" }
            ),
            cancellationToken
        );
        var made = await Connections.CreateAsync(draft, grant, Admin, cancellationToken);
        Assert.AreEqual(AccessStatus.Ok, made.Status, string.Join("; ", made.Errors?.Keys.ToArray() ?? []));
        return made.Value!;
    }

    public ConnectionSubject Subject(int userId, int? tenantId = null) =>
        new(tenantId ?? Tenant, userId, userId == Admin, IsGuest: false);

    public async ValueTask DisposeAsync() => await Services.DisposeAsync();

    private static ServiceProvider Build(
        TestLibrary library,
        FakeProvider provider,
        IReadOnlyDictionary<string, string?>? settings
    )
    {
        var values = library.Settings();
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            values[key] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConnectionProvider>(provider)
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<ICredentialProtector>(new FakeProtector())
            .AddSfeAccess(configuration)
            .BuildServiceProvider();
    }

    private async Task PopulateAsync(TestLibrary library, CancellationToken cancellationToken)
    {
        var accounts = Services.GetRequiredService<AccessService>();
        Tenant = (await accounts.EnsureDefaultTenantAsync("Library", cancellationToken)).Id;
        OtherTenant = (await accounts.CreateTenantAsync(new TenantInput("Other"), library.Root, cancellationToken))
            .Value!
            .Id;
        Users = (await accounts.GetBuiltInGroupAsync(Tenant, BuiltInGroup.Users, cancellationToken))!.Id;
        OtherUsers = (await accounts.GetBuiltInGroupAsync(OtherTenant, BuiltInGroup.Users, cancellationToken))!.Id;
        Team = (await accounts.CreateGroupAsync(Tenant, new GroupInput("Team"), false, cancellationToken)).Value!.Id;
        Alice = await UserAsync(accounts, new UserInput("alice", Password: "alice-password-1", GroupIds: [Users]));
        Bob = await UserAsync(accounts, new UserInput("bob", Password: "bob-password-1", GroupIds: [Team]));
        Carol = await UserAsync(accounts, new UserInput("carol", Password: "carol-password-1", GroupIds: [OtherUsers]));
        Admin = await UserAsync(accounts, new UserInput("root", Password: "root-password-1", IsServerAdmin: true));

        async Task<int> UserAsync(AccessService service, UserInput input) =>
            (await service.CreateUserAsync(input, null, cancellationToken)).Value!.Id;
    }

    private void CopyPeople(ConnectionWorld from)
    {
        (Tenant, OtherTenant, Users, Team, OtherUsers) = (
            from.Tenant,
            from.OtherTenant,
            from.Users,
            from.Team,
            from.OtherUsers
        );
        (Alice, Bob, Carol, Admin) = (from.Alice, from.Bob, from.Carol, from.Admin);
    }
}
