using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Sfe.Api.Endpoints;
using Sfe.Api.Security;
using Sfe.Core.Options;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// What the sign-in limits count a visitor as, what a flood does to them, and what a locked account answers (audit
/// 2026-11, A5 and A7).
/// </summary>
[TestClass]
public sealed class SignInLimitTests
{
    private TestLibrary _library = null!;
    private readonly List<IDisposable> _disposables = [];

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
            disposable.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    [DataRow("203.0.113.7", "203.0.113.7")]
    [DataRow("::ffff:203.0.113.7", "203.0.113.7")]
    [DataRow("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [DataRow("2001:db8:1:2::1", "2001:db8:1:2::/64")]
    [DataRow("2001:db8:1:3::1", "2001:db8:1:3::/64")]
    [DataRow("::1", "::1")]
    [DataRow("127.0.0.1", "127.0.0.1")]
    public void AnAddressCountsAsItselfOrAsItsIpv6Network(string address, string key)
    {
        Assert.AreEqual(key, AddressKeys.Of(IPAddress.Parse(address)));
    }

    [TestMethod]
    public void NoAddressHasAnEmptyKey()
    {
        Assert.AreEqual(string.Empty, AddressKeys.Of((IPAddress?)null));
    }

    [TestMethod]
    public async Task GuessesFromOneIpv6NetworkAreCountedTogether()
    {
        var factory = Factory(settings => settings["Sfe:Security:MaxFailedSignIns"] = "3");
        (await ApiClients.SetUpAsync(factory, TestContext.CancellationToken)).Dispose();

        for (var i = 1; i <= 3; i++)
        {
            using var guesser = ApiClients.CreateFrom(factory, $"2001:db8:1:2::{i}");
            (await SignInAsync(guesser, "wrong-password")).Dispose();
        }
        using var sameNetwork = ApiClients.CreateFrom(factory, "2001:db8:1:2:ffff::9");
        using var blocked = await SignInAsync(sameNetwork, ApiClients.AdminPassword);
        using var otherNetwork = ApiClients.CreateFrom(factory, "2001:db8:1:3::1");
        using var owner = await SignInAsync(otherNetwork, ApiClients.AdminPassword);

        Assert.AreEqual(HttpStatusCode.BadRequest, blocked.StatusCode);
        StringAssert.Contains(
            await blocked.Content.ReadAsStringAsync(TestContext.CancellationToken),
            "Too many failed attempts"
        );
        Assert.AreEqual(HttpStatusCode.OK, owner.StatusCode);
    }

    [TestMethod]
    public async Task OneIpv6NetworkSharesTheSignInsPerMinute()
    {
        var factory = Factory(settings => settings["Sfe:Security:SignInsPerMinute"] = "2");
        (await ApiClients.SetUpAsync(factory, TestContext.CancellationToken)).Dispose();

        var answers = new List<HttpStatusCode>();
        for (var i = 1; i <= 3; i++)
        {
            using var guesser = ApiClients.CreateFrom(factory, $"2001:db8:9:9::{i}");
            using var answer = await SignInAsync(guesser, "wrong-password");
            answers.Add(answer.StatusCode);
        }

        CollectionAssert.AreEqual(
            new[] { HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests },
            answers
        );
    }

    [TestMethod]
    public void AFloodOfGuessesDoesNotLiftTheLockoutsOfOthers()
    {
        var throttle = new SignInThrottle(new Settings(new SecurityOptions { MaxFailedSignIns = 3 }));
        for (var i = 0; i < 3; i++)
            throttle.Failed("admin", "203.0.113.7");
        Assert.IsNotNull(throttle.LockedUntil("admin", "203.0.113.7"));

        for (var i = 0; i < SignInThrottle.MaxEntries + 10; i++)
            throttle.Failed($"name-{i}", "198.51.100.1");

        Assert.IsNotNull(throttle.LockedUntil("admin", "203.0.113.7"), "The flood made room at its own expense");
    }

    [TestMethod]
    public async Task ALockedAccountAnswersAsAnyWrongGuessUnlessThePasswordIsRight()
    {
        var factory = Factory(settings => settings["Sfe:Security:AccountLockoutFailures"] = "3");
        (await ApiClients.SetUpAsync(factory, TestContext.CancellationToken)).Dispose();
        for (var i = 0; i < 3; i++)
        {
            using var guesser = ApiClients.CreateFrom(factory, $"203.0.113.{60 + i}");
            (await SignInAsync(guesser, "wrong-password")).Dispose();
        }

        using var stranger = ApiClients.CreateFrom(factory, "198.51.100.20");
        using var guess = await SignInAsync(stranger, "another-wrong-password");
        using var owner = ApiClients.CreateFrom(factory, "192.168.1.20");
        using var locked = await SignInAsync(owner, ApiClients.AdminPassword);

        StringAssert.Contains(
            await guess.Content.ReadAsStringAsync(TestContext.CancellationToken),
            "Wrong name or password",
            "A locked account must not tell a stranger that its name exists"
        );
        StringAssert.Contains(
            await locked.Content.ReadAsStringAsync(TestContext.CancellationToken),
            "Too many failed attempts"
        );
    }

    private WebApplicationFactory<Program> Factory(Action<IDictionary<string, string?>>? configure = null)
    {
        var factory = ApiClients.CreateFactory(_library, configure);
        _disposables.Add(factory);
        return factory;
    }

    private Task<HttpResponseMessage> SignInAsync(HttpClient client, string password) =>
        client.PostAsJsonAsync(
            $"{V1}/session/login",
            new SignInRequest(ApiClients.AdminName, password),
            TestContext.CancellationToken
        );

    private sealed class Settings(SecurityOptions value) : IOptionsMonitor<SecurityOptions>
    {
        public SecurityOptions CurrentValue => value;

        public SecurityOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<SecurityOptions, string?> listener) => null;
    }
}
