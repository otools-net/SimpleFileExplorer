using Sfe.Core.Access;
using Sfe.Core.Settings;

namespace Sfe.Core.Connections;

/// <summary>
/// Making a connection by signing in to the provider: begun with what the person asks (kind, level, name, method),
/// checked before anyone is sent to the provider, then completed with what the person brings back (OAuth: the code,
/// through the redirect or typed). Who may make a connection at a level is asked by the Api before both calls.
/// </summary>
public sealed class ConnectionSignInFlow(
    ConnectionProviders providers,
    ConnectionService connections,
    PendingSignIns pending,
    SettingLayers settings
)
{
    public const string TooManyProblem = "Too many sign-ins are waiting: try again in a few minutes.";

    public async Task<AccessResult<SignInBegun>> BeginAsync(
        ConnectRequest request,
        SignInContext context,
        CancellationToken cancellationToken = default
    )
    {
        var draft = new ConnectionDraft(
            request.Kind,
            request.Name,
            request.Level,
            request.Level == ConnectionLevel.Account ? context.UserId : null,
            request.Level == ConnectionLevel.Library ? context.TenantId : null,
            request.RootPath
        );
        var checkedDraft = await connections.CheckAsync(draft, cancellationToken);
        if (checkedDraft.Status != AccessStatus.Ok)
            return checkedDraft.As<SignInBegun>();

        var options = settings.ServerLevel().Connections;
        var provider = providers.Find(request.Kind)!;
        if (provider.SignInMethods(options).All(m => m.Kind != request.Method))
            return AccessResult<SignInBegun>.Invalid("method", "This kind of connection does not sign in this way.");

        var state = PendingSignIns.NewState();
        var back = request.Method == SignInKind.OAuthRedirect ? context.ReturnAddress : null;
        SignInStep step;
        try
        {
            step = await provider.BeginSignInAsync(
                new(request.Method, options, back, state, request.Fields ?? new Dictionary<string, string>()),
                cancellationToken
            );
        }
        catch (RemoteStorageException error)
        {
            return AccessResult<SignInBegun>.Conflict(error.Message);
        }

        if (step.Grant is { } grant)
        {
            var made = await connections.CreateAsync(draft, grant, context.UserId, cancellationToken);
            return made.Status == AccessStatus.Ok
                ? AccessResult<SignInBegun>.Ok(new(made.Value))
                : made.As<SignInBegun>();
        }
        if (!pending.Add(new PendingSignIn(state, context.UserId, draft, request.Method, step.Pending, back)))
            return AccessResult<SignInBegun>.Conflict(TooManyProblem);
        return AccessResult<SignInBegun>.Ok(new(null, step.Address, state, step.Fields ?? []));
    }

    /// <summary>
    /// Ends the sign-in of <paramref name="state"/> begun by <paramref name="userId"/> (once: a state is spent by
    /// any attempt) and makes the connection. Not found: unknown, late, used, or another account's.
    /// </summary>
    /// <param name="mayMake">Asked again with the level: the account may have lost the right meanwhile.</param>
    public async Task<AccessResult<ConnectionInfo>> CompleteAsync(
        string? state,
        int userId,
        IReadOnlyDictionary<string, string> fields,
        Func<ConnectionLevel, bool> mayMake,
        CancellationToken cancellationToken = default
    )
    {
        if (pending.Take(state, userId) is not { } signIn || !mayMake(signIn.Draft.Level))
            return AccessResult<ConnectionInfo>.Missing();

        var options = settings.ServerLevel().Connections;
        if (providers.Find(signIn.Draft.Kind) is not { } provider)
            return AccessResult<ConnectionInfo>.Missing();
        ConnectionGrant grant;
        try
        {
            grant = await provider.CompleteSignInAsync(
                new(signIn.Method, options, signIn.Pending, signIn.ReturnAddress, fields),
                cancellationToken
            );
        }
        catch (RemoteStorageException error)
        {
            return AccessResult<ConnectionInfo>.Conflict(error.Message);
        }
        return await connections.CreateAsync(signIn.Draft, grant, userId, cancellationToken);
    }

    /// <summary>The person refused at the provider, or the provider failed: the state is spent, nothing made.</summary>
    public void Abandon(string? state, int userId) => pending.Take(state, userId);
}
