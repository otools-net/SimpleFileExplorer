using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Settings;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// Signing in to a provider to make a connection, or to sign an existing one in again (its sign-in refused or lost):
/// begun with what the person asks, checked before anyone is sent to the provider, then completed with what the
/// person brings back (OAuth: the code, through the redirect or typed). Who may do it is decided by the Api from the
/// draft (level, owner, library), before both calls.
/// </summary>
public sealed class ConnectionSignInFlow(
    ConnectionProviders providers,
    ConnectionService connections,
    PendingSignIns pending,
    SettingLayers settings
)
{
    public const string TooManyProblem = "Too many sign-ins are waiting: try again in a few minutes.";

    public const string MethodProblem = "This kind of connection does not sign in this way.";

    public const string RefusedProblem = "The storage did not accept these details.";

    /// <summary>Begins making a connection.</summary>
    public async Task<AccessResult<SignInBegun>> BeginAsync(
        ConnectRequest request,
        SignInContext context,
        CancellationToken cancellationToken = default
    )
    {
        var draft = DraftOf(request, context);
        var checkedDraft = await connections.CheckAsync(draft, cancellationToken);
        if (checkedDraft.Status != AccessStatus.Ok)
            return checkedDraft.As<SignInBegun>();
        var plan = new SignInPlan(draft, null, request.Method, request.Fields, request.Return);
        return await StartAsync(plan, context, cancellationToken);
    }

    /// <summary>
    /// Whether <see cref="BeginAsync"/> would go through, nothing made nor kept (<c>validateOnly</c>): the draft's
    /// checks and the way to sign in; a way needing no browser (a password) is tried at the provider, its grant
    /// thrown away.
    /// </summary>
    public async Task<AccessResult<bool>> CheckAsync(
        ConnectRequest request,
        SignInContext context,
        CancellationToken cancellationToken = default
    )
    {
        var checkedDraft = await connections.CheckAsync(DraftOf(request, context), cancellationToken);
        if (checkedDraft.Status != AccessStatus.Ok)
            return checkedDraft.As<bool>();
        var options = Options();
        var provider = providers.Find(request.Kind)!;
        if (provider.SignInMethods(options).All(m => m.Kind != request.Method))
            return AccessResult<bool>.Invalid("method", MethodProblem);
        if (request.Method is SignInKind.OAuthRedirect or SignInKind.OAuthCode)
            return AccessResult<bool>.Ok(true);
        try
        {
            var start = new SignInStart(request.Method, options, null, PendingSignIns.NewState(), Fields(request));
            var step = await provider.BeginSignInAsync(start, cancellationToken);
            return step.Grant is null ? AccessResult<bool>.Conflict(RefusedProblem) : AccessResult<bool>.Ok(true);
        }
        catch (RemoteStorageException error)
        {
            return AccessResult<bool>.Conflict(error.Message);
        }
    }

    /// <summary>Where the redirect of <paramref name="state"/>'s sign-in lands, before it ends.</summary>
    public SignInReturn ReturnOf(string? state, int userId) =>
        pending.Peek(state, userId)?.Return ?? SignInReturn.Connections;

    /// <summary>
    /// Begins signing connection <paramref name="connectionId"/> in again; not found when <paramref name="mayManage"/>
    /// refuses its draft (who it belongs to).
    /// </summary>
    public async Task<AccessResult<SignInBegun>> BeginReconnectAsync(
        int connectionId,
        SignInAgain request,
        SignInContext context,
        Func<ConnectionDraft, bool> mayManage,
        CancellationToken cancellationToken = default
    )
    {
        if (await connections.FindAsync(connectionId, cancellationToken) is not { } connection)
            return AccessResult<SignInBegun>.Missing();
        var draft = new ConnectionDraft(
            connection.Kind,
            connection.Name,
            connection.Level,
            connection.OwnerId,
            connection.TenantId,
            connection.RootPath
        );
        if (!mayManage(draft))
            return AccessResult<SignInBegun>.Missing();
        if (providers.Find(connection.Kind) is null)
            return AccessResult<SignInBegun>.Conflict(ConnectionService.UnusableProblem);
        var plan = new SignInPlan(draft, connectionId, request.Method, request.Fields, SignInReturn.Connections);
        return await StartAsync(plan, context, cancellationToken);
    }

    /// <summary>
    /// Ends the sign-in of <paramref name="state"/> begun by <paramref name="userId"/> (once: a state is spent by
    /// any attempt). Not found: unknown, late, used, another account's, or <paramref name="mayManage"/> refuses it now.
    /// </summary>
    /// <param name="mayManage">Asked again with the draft: the account may have lost the right meanwhile.</param>
    public async Task<AccessResult<SignInDone>> CompleteAsync(
        string? state,
        int userId,
        IReadOnlyDictionary<string, string> fields,
        Func<ConnectionDraft, bool> mayManage,
        CancellationToken cancellationToken = default
    )
    {
        if (pending.Take(state, userId) is not { } signIn || !mayManage(signIn.Draft))
            return AccessResult<SignInDone>.Missing();
        if (providers.Find(signIn.Draft.Kind) is not { } provider)
            return AccessResult<SignInDone>.Missing();

        ConnectionGrant grant;
        try
        {
            grant = await provider.CompleteSignInAsync(
                new(signIn.Method, Options(), signIn.Pending, signIn.ReturnAddress, fields),
                cancellationToken
            );
        }
        catch (RemoteStorageException error)
        {
            return AccessResult<SignInDone>.Conflict(error.Message);
        }
        return await FinishAsync(signIn.Draft, signIn.ConnectionId, grant, userId, cancellationToken);
    }

    /// <summary>The person refused at the provider, or it failed: the state is spent, nothing changed.</summary>
    public void Abandon(string? state, int userId) => pending.Take(state, userId);

    private async Task<AccessResult<SignInBegun>> StartAsync(
        SignInPlan plan,
        SignInContext context,
        CancellationToken cancellationToken
    )
    {
        var options = Options();
        var provider = providers.Find(plan.Draft.Kind)!;
        if (provider.SignInMethods(options).All(m => m.Kind != plan.Method))
            return AccessResult<SignInBegun>.Invalid("method", MethodProblem);

        var state = PendingSignIns.NewState();
        var back = plan.Method == SignInKind.OAuthRedirect ? context.ReturnAddress : null;
        SignInStep step;
        try
        {
            step = await provider.BeginSignInAsync(
                new(plan.Method, options, back, state, plan.Fields ?? new Dictionary<string, string>()),
                cancellationToken
            );
        }
        catch (RemoteStorageException error)
        {
            return AccessResult<SignInBegun>.Conflict(error.Message);
        }

        if (step.Grant is { } grant)
        {
            var done = await FinishAsync(plan.Draft, plan.ConnectionId, grant, context.UserId, cancellationToken);
            return done.Status == AccessStatus.Ok
                ? AccessResult<SignInBegun>.Ok(new(done.Value!.Connection))
                : done.As<SignInBegun>();
        }
        var waiting = new PendingSignIn(
            state,
            context.UserId,
            plan.Draft,
            plan.Method,
            step.Pending,
            back,
            plan.ConnectionId,
            Return: plan.Return
        );
        if (!pending.Add(waiting))
            return AccessResult<SignInBegun>.Conflict(TooManyProblem);
        return AccessResult<SignInBegun>.Ok(new(null, step.Address, state, step.Fields ?? []));
    }

    private async Task<AccessResult<SignInDone>> FinishAsync(
        ConnectionDraft draft,
        int? connectionId,
        ConnectionGrant grant,
        int userId,
        CancellationToken cancellationToken
    )
    {
        if (connectionId is { } id)
        {
            var again = await connections.ReauthorizeAsync(id, grant, userId, cancellationToken);
            return again.Status == AccessStatus.Ok
                ? AccessResult<SignInDone>.Ok(new(again.Value!.Connection, true, again.Value.OtherAccount))
                : again.As<SignInDone>();
        }
        var made = await connections.CreateAsync(draft, grant, userId, cancellationToken);
        return made.Status == AccessStatus.Ok ? AccessResult<SignInDone>.Ok(new(made.Value!)) : made.As<SignInDone>();
    }

    private Options.ConnectionOptions Options() => settings.ServerLevel().Connections;

    /// <summary>What a request asks to make: its owner or library from the session, its groups the library's.</summary>
    private static ConnectionDraft DraftOf(ConnectRequest request, SignInContext context) =>
        new(
            request.Kind,
            request.Name,
            request.Level,
            request.Level == ConnectionLevel.Account ? context.UserId : null,
            request.Level == ConnectionLevel.Library ? context.TenantId : null,
            request.RootPath,
            request.Groups is { Count: > 0 } groups ? new ConnectionUses(context.TenantId, groups) : null
        );

    private static IReadOnlyDictionary<string, string> Fields(ConnectRequest request) =>
        request.Fields ?? new Dictionary<string, string>();

    /// <summary>A sign-in about to start: for a new connection, or for <see cref="ConnectionId"/>.</summary>
    private sealed record SignInPlan(
        ConnectionDraft Draft,
        int? ConnectionId,
        SignInKind Method,
        IReadOnlyDictionary<string, string>? Fields,
        SignInReturn Return
    );
}
