namespace Otools.Net.Sfe.Core.Data;

/// <summary>
/// How an <see cref="AccessToken"/> was given: only personal tokens exist; the kind leaves room for others (OAuth).
/// </summary>
public enum AccessTokenKind
{
    /// <summary>Made by an account for itself, on its account page.</summary>
    Personal,
}
