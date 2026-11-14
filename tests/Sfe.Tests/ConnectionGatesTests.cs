using Sfe.Core.Connections;

namespace Sfe.Tests;

/// <summary>
/// <see cref="ConnectionGates"/>: the provider's wait kept per connection, and at most
/// <see cref="ConnectionGates.MaxCalls"/> calls at once through one connection.
/// </summary>
[TestClass]
public sealed class ConnectionGatesTests
{
    private readonly SteppedClock _clock = new();
    private ConnectionGates _gates = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize() => _gates = new ConnectionGates(_clock);

    [TestMethod]
    public void AWaitHoldsItsConnectionUntilItEnds()
    {
        Assert.IsNull(_gates.WaitLeft(1));

        _gates.Wait(1, TimeSpan.FromSeconds(30));
        _clock.Now += TimeSpan.FromSeconds(10);

        Assert.AreEqual(TimeSpan.FromSeconds(20), _gates.WaitLeft(1));
        Assert.IsNull(_gates.WaitLeft(2));
        _clock.Now += TimeSpan.FromSeconds(20);
        Assert.IsNull(_gates.WaitLeft(1));
    }

    [TestMethod]
    public void AWaitWithoutLengthTakesTheDefaultAndNeverShortensALongerOne()
    {
        _gates.Wait(1, null);
        _gates.Wait(2, TimeSpan.Zero);
        _gates.Wait(3, TimeSpan.FromMinutes(1));
        _gates.Wait(3, TimeSpan.FromSeconds(1));

        Assert.AreEqual(ConnectionGates.DefaultWait, _gates.WaitLeft(1));
        Assert.AreEqual(ConnectionGates.DefaultWait, _gates.WaitLeft(2));
        Assert.AreEqual(TimeSpan.FromMinutes(1), _gates.WaitLeft(3));
    }

    [TestMethod]
    public async Task AConnectionGivesAFewTurnsAtOnceAndTheNextWaits()
    {
        var turns = new List<IDisposable>();
        for (var n = 0; n < ConnectionGates.MaxCalls; n++)
            turns.Add((await _gates.EnterAsync(1, Token))!);

        var next = _gates.EnterAsync(1, Token);
        await Task.Delay(100, Token);
        Assert.IsFalse(next.IsCompleted, "every turn is taken");
        using (var other = await _gates.EnterAsync(2, Token))
            Assert.IsNotNull(other, "another connection has its own turns");

        turns[0].Dispose();
        using var entered = await next.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.IsNotNull(entered);

        // Disposed again: no second turn given back.
        turns[0].Dispose();
        var more = _gates.EnterAsync(1, Token);
        await Task.Delay(100, Token);
        Assert.IsFalse(more.IsCompleted);
        turns[1].Dispose();
        using var last = await more.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.IsNotNull(last);
        foreach (var turn in turns.Skip(2))
            turn.Dispose();
    }

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
