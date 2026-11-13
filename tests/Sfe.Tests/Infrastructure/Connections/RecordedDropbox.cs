using System.Net;
using System.Text;

namespace Sfe.Tests.Infrastructure;

/// <summary>
/// Dropbox's answers, recorded (shapes from the SDK's spike, agents/docs/development/connections-plan.md): the
/// transport given to <see cref="Sfe.Core.Connections.OutboundPolicy"/>, so no test calls Dropbox. Keeps every
/// request with its body, and answers by path.
/// </summary>
public sealed class RecordedDropbox : HttpMessageHandler
{
    public const string TokenAnswer = """
        {"access_token":"sl.access","token_type":"bearer","expires_in":14400,"refresh_token":"refresh-1",
         "scope":"account_info.read files.content.read","uid":"12345","account_id":"dbid:alice"}
        """;

    public const string AccountAnswer = """
        {"account_id":"dbid:alice","name":{"given_name":"Alice","surname":"A","familiar_name":"Alice",
         "display_name":"Alice A","abbreviated_name":"AA"},"email":"alice@example.org","email_verified":true,
         "disabled":false,"locale":"en","referral_link":"https://referral.example/x","is_paired":false,
         "account_type":{".tag":"basic"},"root_info":{".tag":"user","root_namespace_id":"1","home_namespace_id":"1"}}
        """;

    public const string AuthErrorAnswer = """
        {"error_summary":"invalid_access_token/","error":{".tag":"invalid_access_token"}}
        """;

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    /// <summary>Answers by the request's path; a path not listed answers 404.</summary>
    public Dictionary<string, (HttpStatusCode Status, string Body)> Answers { get; } =
        new()
        {
            ["/oauth2/token"] = (HttpStatusCode.OK, TokenAnswer),
            ["/2/users/get_current_account"] = (HttpStatusCode.OK, AccountAnswer),
            ["/2/check/app"] = (HttpStatusCode.OK, """{"result":"sfe"}"""),
        };

    public (HttpRequestMessage Request, string Body) Last(string path) =>
        Requests.Last(r => r.Request.RequestUri!.AbsolutePath == path);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        if (!Answers.TryGetValue(request.RequestUri!.AbsolutePath, out var answer))
            return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };

        return new HttpResponseMessage(answer.Status)
        {
            RequestMessage = request,
            Content = new StringContent(answer.Body, Encoding.UTF8, "application/json"),
        };
    }
}
