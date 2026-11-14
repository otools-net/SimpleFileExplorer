using Sfe.Core.Connections;
using Sfe.Core.Transfers;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// <see cref="TransferCalls"/>: a provider down is tried again with a growing backoff, then given up; a provider
/// slowing the account down is waited for; other failures are thrown at once; writes go one at a time.
/// </summary>
[TestClass]
public sealed class TransferCallsTests
{
    private const int Connection = 1;

    private readonly FakeProvider _provider = new();
    private readonly InstantClock _clock = new();
    private ConnectionGates _gates = null!;
    private TransferCalls _calls = null!;
    private FakeAccount _account = null!;
    private IRemoteStorage _storage = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize()
    {
        _gates = new ConnectionGates(_clock);
        _calls = new TransferCalls(_gates, _clock);
        _account = _provider.Account("alice").WithFile("a.txt", "a");
        _storage = _provider.Open("alice", "", new());
    }

    [TestCleanup]
    public async Task CleanupAsync() => await _storage.DisposeAsync();

    [TestMethod]
    public async Task AProviderDownIsTriedAgainWithAGrowingBackoff()
    {
        _account.FailNext("get", RemoteErrorKind.Unavailable, 3);

        var item = await GetAsync();

        Assert.AreEqual("a.txt", item?.Name);
        Assert.AreEqual(4, _account.Calls.Count(c => c == "get a.txt"));
        CollectionAssert.AreEqual(Seconds(1, 2, 4), _clock.Waits.ToList());
    }

    [TestMethod]
    public async Task AProviderDownIsGivenUpAfterFiveRetries()
    {
        _account.FailNext("get", RemoteErrorKind.Unavailable, 6);

        var failure = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() => GetAsync());

        Assert.AreEqual(RemoteErrorKind.Unavailable, failure.Kind);
        Assert.HasCount(TransferCalls.MaxRetries + 1, _account.Calls);
        CollectionAssert.AreEqual(Seconds(1, 2, 4, 8, 16), _clock.Waits.ToList());
    }

    [TestMethod]
    public async Task ARateLimitWaitsTheProvidersTimeThenGoesOn()
    {
        _account.RetryAfter = TimeSpan.FromSeconds(30);
        _account.FailNext("get", RemoteErrorKind.RateLimited);

        var item = await GetAsync();

        Assert.IsNotNull(item);
        CollectionAssert.AreEqual(Seconds(30), _clock.Waits.ToList());
        Assert.IsNull(_gates.WaitLeft(Connection));
    }

    [TestMethod]
    public async Task AWaitAskedThroughAnotherCallHoldsThisOne()
    {
        _gates.Wait(Connection, TimeSpan.FromSeconds(10));

        await GetAsync();

        CollectionAssert.AreEqual(Seconds(10), _clock.Waits.ToList());
    }

    [TestMethod]
    public async Task AnEndedSignInIsThrownAtOnce()
    {
        _account.FailNext("get", RemoteErrorKind.CredentialsEnded, 3);

        var failure = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() => GetAsync());

        Assert.AreEqual(RemoteErrorKind.CredentialsEnded, failure.Kind);
        Assert.HasCount(1, _account.Calls);
        Assert.IsEmpty(_clock.Waits);
    }

    [TestMethod]
    public async Task WritesThroughOneConnectionGoOneAtATime()
    {
        var first = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var firstWrite = _calls.WriteAsync(
            Connection,
            async _ =>
            {
                started.SetResult();
                await first.Task;
            },
            Token
        );
        await started.Task;
        var secondRan = false;
        var secondWrite = _calls.WriteAsync(Connection, _ => Task.FromResult(secondRan = true), Token);
        var otherConnection = _calls.WriteAsync(Connection + 1, _ => Task.FromResult(true), Token);

        Assert.IsTrue(await otherConnection);
        Assert.IsFalse(secondRan);
        first.SetResult();
        await firstWrite;
        Assert.IsTrue(await secondWrite);
    }

    private Task<RemoteItem?> GetAsync() =>
        _calls.RunAsync(Connection, token => _storage.GetAsync("a.txt", token), Token);

    private static List<TimeSpan> Seconds(params int[] seconds) => [.. seconds.Select(s => TimeSpan.FromSeconds(s))];
}
