using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>The tests' waits on slow work (<see cref="SlowWork"/>): too slow is inconclusive, never a failure.</summary>
[TestClass]
public sealed class SlowWorkTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task WorkTooSlowMakesTheTestInconclusive()
    {
        var reads = 0;

        var inconclusive = await Assert.ThrowsExactlyAsync<AssertInconclusiveException>(() =>
            SlowWork.UntilAsync(() => ++reads, _ => false, 0.2 / SlowWork.Factor, "nothing", TestContext.CancellationToken));
        var done = await SlowWork.UntilAsync(() => ++reads, n => n > reads - 1, 5, "the next read", TestContext.CancellationToken);

        StringAssert.Contains(inconclusive.Message, SlowWork.FactorVariable);
        Assert.IsGreaterThan(1, reads, "Read until the limit");
        Assert.AreEqual(reads, done);
    }
}
