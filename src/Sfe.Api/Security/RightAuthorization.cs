namespace Otools.Net.Sfe.Api.Security;

public static class RightAuthorization
{
    private const string PolicyPrefix = "right:";

    /// <summary>
    /// The route needs one of <paramref name="anyOf"/> in the request's library (<see cref="CurrentAccess"/>).
    /// Visitors without an account pass too when the library's guests have it; otherwise they get 401, and accounts
    /// without the right 403.
    /// </summary>
    public static TBuilder RequireRight<TBuilder>(this TBuilder builder, params string[] anyOf)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RightMetadata(anyOf)).RequireAuthorization(PolicyPrefix + string.Join(',', anyOf));

    internal static IReadOnlyList<string>? RightsOf(string policyName) =>
        policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal)
            ? policyName[PolicyPrefix.Length..].Split(',')
            : null;
}
