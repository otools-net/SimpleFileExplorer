using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <see cref="AccessTokens"/>: the secret shown once and only its hash kept, tokens found only while valid, the
/// limits of the settings, the rights among the account's, and each account's tokens kept apart.
/// </summary>
[TestClass]
public sealed class AccessTokenServiceTests
{
    private const string Address = "192.0.2.7";

    private TestLibrary _library = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task TheSecretIsShownOnceAndOnlyItsHashIsKept()
    {
        await using var world = await ConnectionWorld.CreateAsync(_library, Token);
        var tokens = Tokens(world);

        var created = await CreateAsync(tokens, world.Alice, new("Backup script"));

        Assert.StartsWith(AccessTokenSecret.Scheme, created.Secret);
        Assert.HasCount(AccessTokenSecret.Length, created.Secret);
        Assert.StartsWith(created.Token.Prefix, created.Secret);
        await using var db = world.Db();
        var row = await db.AccessTokens.SingleAsync(Token);
        var stored = string.Join(" ", row.Name, row.Lookup, row.Prefix, row.Rights, Convert.ToBase64String(row.Hash));
        Assert.DoesNotContain(created.Secret[created.Token.Prefix.Length..], stored);
        Assert.HasCount(AccessToken.HashLength, row.Hash);
        Assert.AreEqual(("Backup script", "alice"), (created.Token.Name, created.Token.UserName));

        var found = await tokens.FindAsync(created.Secret, Address, Token);
        Assert.AreEqual((created.Token.Id, world.Alice), (found!.Id, found.UserId));
        Assert.IsNull(found.Rights);
    }

    [TestMethod]
    public async Task WrongMalformedExpiredAndRevokedTokensFindNothing()
    {
        await using var world = await ConnectionWorld.CreateAsync(_library, Token);
        var tokens = Tokens(world);
        var created = await CreateAsync(tokens, world.Alice, new("Script"));
        var secret = created.Secret;
        var lastSecretCharacter = secret[^2] == 'A' ? 'B' : 'A';
        string?[] wrong =
        [
            null,
            "",
            secret[..^1],
            secret[..^2] + lastSecretCharacter + secret[^1],
            "sfe_ffffffffffffffff" + secret[20..],
            secret.ToUpperInvariant(),
            "Bearer " + secret,
        ];

        foreach (var presented in wrong)
            Assert.IsNull(await tokens.FindAsync(presented, Address, Token), presented);

        await using (var db = world.Db())
        {
            await db.AccessTokens.ExecuteUpdateAsync(s => s.SetProperty(t => t.Expires, DateTime.UtcNow), Token);
        }
        world.Services.GetRequiredService<AccessChanges>().Changed();
        Assert.IsNull(await tokens.FindAsync(secret, Address, Token));

        var revoked = await CreateAsync(tokens, world.Alice, new("Revoked"));
        Assert.IsNotNull(await tokens.FindAsync(revoked.Secret, Address, Token));
        Assert.AreEqual(AccessStatus.Ok, await tokens.RevokeAsync(revoked.Token.Id, world.Alice, world.Alice, Token));
        Assert.IsNull(await tokens.FindAsync(revoked.Secret, Address, Token));
        var listed = (await tokens.ListAsync(world.Alice, Token)).Single(t => t.Id == revoked.Token.Id);
        Assert.AreEqual(world.Alice, listed.RevokedById);
        Assert.IsNotNull(listed.Revoked);
    }

    [TestMethod]
    public async Task TurnedOffNoTokenIsMadeNorFound()
    {
        await using var world = await ConnectionWorld.CreateAsync(_library, Token);
        var made = await CreateAsync(Tokens(world), world.Alice, new("Script"));
        await using var off = world.WithSettings(_library, Setting("Enabled", "false"));
        var tokens = Tokens(off);

        var refused = await tokens.CreateAsync(off.Alice, new("Another"), Token);

        Assert.AreEqual((AccessStatus.Conflict, AccessTokens.DisabledProblem), (refused.Status, refused.Problem));
        Assert.IsNull(await tokens.FindAsync(made.Secret, Address, Token));
        Assert.HasCount(1, await tokens.ListAsync(off.Alice, Token));
    }

    [TestMethod]
    public async Task AnAccountHoldsAtMostTheTokensAllowed()
    {
        await using var start = await ConnectionWorld.CreateAsync(_library, Token);
        await using var world = start.WithSettings(_library, Setting("MaxPerAccount", "2"));
        var tokens = Tokens(world);
        var first = await CreateAsync(tokens, world.Alice, new("One"));
        await CreateAsync(tokens, world.Alice, new("Two"));

        var third = await tokens.CreateAsync(world.Alice, new("Three"), Token);
        Assert.AreEqual((AccessStatus.Conflict, AccessTokens.TooManyProblem), (third.Status, third.Problem));
        // Another account has its own count; a revoked token no longer counts.
        await CreateAsync(tokens, world.Bob, new("Bob's"));
        await tokens.RevokeAsync(first.Token.Id, world.Alice, world.Alice, Token);
        await CreateAsync(tokens, world.Alice, new("Three"));
    }

    [TestMethod]
    public async Task ATokenLivesNoLongerThanAllowed()
    {
        await using var start = await ConnectionWorld.CreateAsync(_library, Token);
        await using var world = start.WithSettings(_library, Setting("MaxLifetimeDays", "30"));
        var tokens = Tokens(world);
        var now = DateTime.UtcNow;

        var longest = await CreateAsync(tokens, world.Alice, new("Default"));
        var shorter = await CreateAsync(tokens, world.Alice, new("Week", now.AddDays(7)));
        var tooLong = await tokens.CreateAsync(world.Alice, new("Year", now.AddDays(31)), Token);
        var past = await tokens.CreateAsync(world.Alice, new("Past", now.AddMinutes(-1)), Token);

        Assert.AreEqual(30, Math.Round((longest.Token.Expires!.Value - now).TotalDays));
        Assert.AreEqual(7, Math.Round((shorter.Token.Expires!.Value - now).TotalDays));
        Assert.IsTrue(tooLong.Errors!.ContainsKey("expires"));
        Assert.IsTrue(past.Errors!.ContainsKey("expires"));

        await using var unlimited = start.WithSettings(_library, Setting("MaxLifetimeDays", "0"));
        Assert.IsNull((await CreateAsync(Tokens(unlimited), unlimited.Alice, new("Forever"))).Token.Expires);
    }

    [TestMethod]
    public async Task ATokensRightsAndLibraryAreAmongTheAccounts()
    {
        await using var world = await ConnectionWorld.CreateAsync(_library, Token);
        var tokens = Tokens(world);
        var evaluator = world.Services.GetRequiredService<RightsEvaluator>();
        var held = (await evaluator.GetAsync(new(world.Tenant, world.Alice), Token)).Rights.Order().Take(2).ToList();
        Assert.HasCount(2, held);

        var limited = await CreateAsync(tokens, world.Alice, new("Limited", TenantId: world.Tenant, Rights: held));
        var notHeld = await tokens.CreateAsync(world.Alice, new("Admin", Rights: [Rights.ServerSettings]), Token);
        var unknown = await tokens.CreateAsync(world.Alice, new("Unknown", Rights: ["no.such.right"]), Token);
        var otherLibrary = await tokens.CreateAsync(world.Alice, new("Other", TenantId: world.OtherTenant), Token);

        CollectionAssert.AreEquivalent(held, limited.Token.Rights.ToList());
        var found = await tokens.FindAsync(limited.Secret, Address, Token);
        Assert.AreEqual(world.Tenant, found!.TenantId);
        CollectionAssert.AreEquivalent(held, found.Rights!.ToList());
        Assert.IsTrue(notHeld.Errors!.ContainsKey("rights"));
        Assert.IsTrue(unknown.Errors!.ContainsKey("rights"));
        Assert.IsTrue(otherLibrary.Errors!.ContainsKey("tenantId"));
        Assert.IsTrue((await tokens.CreateAsync(world.Alice, new("  "), Token)).Errors!.ContainsKey("name"));
    }

    [TestMethod]
    public async Task EachAccountSeesAndRevokesItsOwnTokensOnly()
    {
        await using var world = await ConnectionWorld.CreateAsync(_library, Token);
        var tokens = Tokens(world);
        var alices = new[]
        {
            await CreateAsync(tokens, world.Alice, new("One")),
            await CreateAsync(tokens, world.Alice, new("Two")),
        };
        var bobs = await CreateAsync(tokens, world.Bob, new("Bob's"));
        var carols = await CreateAsync(tokens, world.Carol, new("Carol's"));

        CollectionAssert.AreEqual(new[] { "Two", "One" }, Names(await tokens.ListAsync(world.Alice, Token)));
        Assert.HasCount(4, await tokens.ListAllAsync(null, Token));
        CollectionAssert.AreEqual(new[] { "Carol's" }, Names(await tokens.ListAllAsync([world.OtherTenant], Token)));
        Assert.AreEqual(
            AccessStatus.NotFound,
            await tokens.RevokeAsync(bobs.Token.Id, world.Alice, world.Alice, Token)
        );
        Assert.IsNotNull(await tokens.FindAsync(bobs.Secret, Address, Token));

        Assert.AreEqual(2, await tokens.RevokeAllAsync(world.Alice, world.Admin, Token));

        foreach (var alice in alices)
            Assert.IsNull(await tokens.FindAsync(alice.Secret, Address, Token));
        Assert.IsNotNull(await tokens.FindAsync(bobs.Secret, Address, Token));
        Assert.IsNotNull(await tokens.FindAsync(carols.Secret, Address, Token));
        Assert.AreEqual(0, await tokens.RevokeAllAsync(world.Alice, world.Admin, Token));
    }

    [TestMethod]
    public async Task TheLastUseIsWrittenAtMostEveryFewMinutes()
    {
        await using var world = await ConnectionWorld.CreateAsync(_library, Token);
        var tokens = Tokens(world);
        var created = await CreateAsync(tokens, world.Alice, new("Script"));

        await tokens.FindAsync(created.Secret, Address, Token);
        var used = (await tokens.ListAsync(world.Alice, Token)).Single();
        await using (var db = world.Db())
        {
            await db.AccessTokens.ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsed, (DateTime?)null), Token);
        }
        await tokens.FindAsync(created.Secret, "192.0.2.8", Token);

        Assert.AreEqual(Address, used.LastUsedFrom);
        Assert.IsNotNull(used.LastUsed);
        var again = (await tokens.ListAsync(world.Alice, Token)).Single();
        Assert.AreEqual((null, Address), (again.LastUsed, again.LastUsedFrom));
    }

    private static AccessTokens Tokens(ConnectionWorld world) => world.Services.GetRequiredService<AccessTokens>();

    private static Dictionary<string, string?> Setting(string name, string value) =>
        new() { [$"Sfe:Security:AccessTokens:{name}"] = value };

    private static string[] Names(IEnumerable<AccessTokenInfo> tokens) => [.. tokens.Select(t => t.Name)];

    private async Task<CreatedAccessToken> CreateAsync(AccessTokens tokens, int userId, AccessTokenDraft draft)
    {
        var created = await tokens.CreateAsync(userId, draft, Token);
        Assert.AreEqual(
            AccessStatus.Ok,
            created.Status,
            string.Join("; ", created.Errors?.Keys.ToArray() ?? []) + created.Problem
        );
        return created.Value!;
    }
}
