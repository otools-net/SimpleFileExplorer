using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// <see cref="ConnectionSignInFlow"/> and <see cref="PendingSignIns"/>: a sign-in checked before the person leaves,
/// its state used once, by the account that began it, in time.
/// </summary>
[TestClass]
public sealed class ConnectionSignInFlowTests
{
    private const string Back = "https://sfe.example/api/connections/oauth/callback";

    private TestLibrary _library = null!;
    private ConnectionWorld _world = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _world = await ConnectionWorld.CreateAsync(_library, Token);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _world.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AnOAuthSignInMakesTheConnectionOnceTheCodeComesBack()
    {
        var begun = await BeginAsync(_world.Alice, Request(ConnectionLevel.Account, SignInKind.OAuthRedirect));

        StringAssert.StartsWith(begun.Address, FakeProvider.AuthorizeAddress);
        StringAssert.Contains(begun.Address, Uri.EscapeDataString(Back));
        Assert.IsNull(begun.Connection);
        Assert.IsGreaterThanOrEqualTo(40, begun.State!.Length);

        var made = await CompleteAsync(begun.State, _world.Alice, "good:alice");
        Assert.AreEqual(AccessStatus.Ok, made.Status);
        Assert.AreEqual(_world.Alice, made.Value!.OwnerId);
        Assert.AreEqual("alice@fake", made.Value.Account);

        Assert.AreEqual(AccessStatus.NotFound, (await CompleteAsync(begun.State, _world.Alice, "good:alice")).Status);
        await using var db = _world.Db();
        Assert.AreEqual(1, await db.Connections.CountAsync(Token));
    }

    [TestMethod]
    public async Task AStateIsOnlyItsAccountsAndIsSpentByAnyAttempt()
    {
        var begun = await BeginAsync(_world.Alice, Request(ConnectionLevel.Account, SignInKind.OAuthCode));

        Assert.AreEqual(AccessStatus.NotFound, (await CompleteAsync(begun.State, _world.Bob, "good:bob")).Status);
        Assert.AreEqual(AccessStatus.NotFound, (await CompleteAsync(begun.State, _world.Alice, "good:alice")).Status);
        Assert.AreEqual(AccessStatus.NotFound, (await CompleteAsync("forged", _world.Alice, "good:alice")).Status);

        var refused = await BeginAsync(_world.Alice, Request(ConnectionLevel.Account, SignInKind.OAuthCode));
        var wrongCode = await CompleteAsync(refused.State, _world.Alice, "bad");
        Assert.AreEqual(AccessStatus.Conflict, wrongCode.Status);
        Assert.AreEqual(AccessStatus.NotFound, (await CompleteAsync(refused.State, _world.Alice, "good:alice")).Status);

        var abandoned = await BeginAsync(_world.Alice, Request(ConnectionLevel.Account, SignInKind.OAuthRedirect));
        _world.SignIns.Abandon(abandoned.State, _world.Alice);
        Assert.AreEqual(AccessStatus.NotFound, (await CompleteAsync(abandoned.State, _world.Alice, "good:a")).Status);
    }

    [TestMethod]
    public async Task WhatCannotBeMadeIsRefusedBeforeThePersonLeaves()
    {
        var context = new SignInContext(_world.Alice, _world.Tenant, Back);
        ConnectRequest[] wrong =
        [
            Request(ConnectionLevel.Account, SignInKind.OAuthCode) with
            {
                Name = " ",
            },
            Request(ConnectionLevel.Account, SignInKind.OAuthCode) with
            {
                Kind = "nope",
            },
            Request(ConnectionLevel.Account, SignInKind.OAuthCode) with
            {
                RootPath = "../up",
            },
            Request(ConnectionLevel.Account, SignInKind.Key),
        ];
        foreach (var request in wrong)
            Assert.AreEqual(AccessStatus.Invalid, (await _world.SignIns.BeginAsync(request, context, Token)).Status);

        Assert.AreEqual(0, _world.Services.GetService(typeof(PendingSignIns)) is PendingSignIns p ? p.Count : -1);
        Assert.IsEmpty(_world.Provider.Accounts);
    }

    [TestMethod]
    public async Task APasswordSignInIsDoneAtOnceAndTheLibraryIsTheRequests()
    {
        var request = Request(ConnectionLevel.Library, SignInKind.Password) with
        {
            Fields = new Dictionary<string, string> { ["name"] = "shared", ["password"] = "secret" },
        };

        var begun = await BeginAsync(_world.Alice, request);

        Assert.IsNull(begun.State);
        Assert.AreEqual(_world.Tenant, begun.Connection!.TenantId);
        Assert.IsNull(begun.Connection.OwnerId);
    }

    [TestMethod]
    public async Task ALostRightEndsTheSignIn()
    {
        var begun = await BeginAsync(_world.Alice, Request(ConnectionLevel.Server, SignInKind.OAuthCode));

        var made = await _world.SignIns.CompleteAsync(
            begun.State,
            _world.Alice,
            new Dictionary<string, string> { ["code"] = "good:x" },
            level => level != ConnectionLevel.Server,
            Token
        );

        Assert.AreEqual(AccessStatus.NotFound, made.Status);
    }

    [TestMethod]
    public void APendingSignInIsForgottenAfterTenMinutes()
    {
        var clock = new SteppedClock();
        var pending = new PendingSignIns(clock);
        var draft = new ConnectionDraft(FakeProvider.FakeKind, "N", ConnectionLevel.Account, 1);
        pending.Add(new PendingSignIn("late", 1, draft, SignInKind.OAuthCode, "v", null));
        pending.Add(new PendingSignIn("soon", 1, draft, SignInKind.OAuthCode, "v", null));

        clock.Now += TimeSpan.FromMinutes(9);
        Assert.IsNotNull(pending.Take("soon", 1));
        clock.Now += PendingSignIns.Lifetime;
        Assert.IsNull(pending.Take("late", 1));

        for (var i = 0; i < PendingSignIns.MaxPending; i++)
            Assert.IsTrue(pending.Add(new PendingSignIn($"s{i}", 1, draft, SignInKind.OAuthCode, "v", null)));
        Assert.IsFalse(pending.Add(new PendingSignIn("one-too-many", 1, draft, SignInKind.OAuthCode, "v", null)));
        clock.Now += PendingSignIns.Lifetime;
        Assert.IsTrue(pending.Add(new PendingSignIn("after", 1, draft, SignInKind.OAuthCode, "v", null)));
        Assert.AreEqual(1, pending.Count);
    }

    private ConnectRequest Request(ConnectionLevel level, SignInKind method) =>
        new(FakeProvider.FakeKind, level, "Mine", method);

    private async Task<SignInBegun> BeginAsync(int userId, ConnectRequest request)
    {
        var begun = await _world.SignIns.BeginAsync(request, new(userId, _world.Tenant, Back), Token);
        Assert.AreEqual(AccessStatus.Ok, begun.Status, string.Join("; ", begun.Errors?.Keys.ToArray() ?? []));
        return begun.Value!;
    }

    private Task<AccessResult<ConnectionInfo>> CompleteAsync(string? state, int userId, string code) =>
        _world.SignIns.CompleteAsync(
            state,
            userId,
            new Dictionary<string, string> { ["code"] = code },
            _ => true,
            Token
        );

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
