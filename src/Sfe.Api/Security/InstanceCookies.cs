namespace Sfe.Api.Security;

/// <summary>
/// Cookie names of their own for each port: browsers keep cookies per host, not per port, so two instances on one host
/// (<c>localhost:5000</c> and <c>localhost:5001</c>) would otherwise share the session cookie, each sign-in replacing
/// the other's (which the other's keys can't read: signed out). On the default port the names stay as they were.
/// </summary>
public static class InstanceCookies
{
    /// <summary>
    /// <paramref name="name"/> for the port the browser asked for (its Host): <c>sfe.session-5000</c>.
    /// </summary>
    public static string Name(HttpRequest request, string name) =>
        request.Host.Port is { } port ? $"{name}-{port}" : name;
}
