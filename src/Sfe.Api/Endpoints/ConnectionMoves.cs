using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Logs;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// Renames and moves inside a connection with its folder rules following, for every library, in the library's order
/// (agents/docs/features/connections.md): the rules begin to move (the connection's lock), the provider moves, then
/// the move completes or aborts. A move letting more of the request's library's people see or change a folder is
/// asked about first (409 <see cref="AccessGainsType"/>, the library's question); other libraries' rules follow
/// with a security warning. A connection without rules is moved as before, without a word to the database.
/// </summary>
internal sealed class ConnectionMoves
{
    /// <summary>The problem type of a move letting more people see or change a folder (its <c>gains</c>).</summary>
    public const string AccessGainsType = ProblemTypes.AccessGains;

    /// <summary>Numbered names tried for a move keeping both (<see cref="PlaceAsync"/>).</summary>
    private const int MaxNumbered = 20;

    private readonly OpenConnection _open;
    private readonly ConnectionWriteServices _writes;
    private readonly AccessState _state;
    private readonly bool? _keepAccess;

    private ConnectionMoves(OpenConnection open, ConnectionWriteServices writes, AccessState state, bool? keepAccess)
    {
        (_open, _writes, _state, _keepAccess) = (open, writes, state, keepAccess);
    }

    /// <summary>Some library set rules in the connection: moves go through them.</summary>
    public bool Ruled { get; private init; }

    /// <param name="keepAccess">The answer to the question: null when not asked yet.</param>
    public static async Task<ConnectionMoves> CreateAsync(
        OpenConnection open,
        ConnectionWriteServices writes,
        CurrentAccess current,
        bool? keepAccess,
        CancellationToken cancellationToken
    )
    {
        var state = await current.GetAsync();
        var ruled = await writes.Rules.HasRulesAsync(open.Connection.Id, cancellationToken);
        return new ConnectionMoves(open, writes, state, keepAccess) { Ruled = ruled };
    }

    /// <summary>
    /// The 409 when one of <paramref name="moves"/> (folders the session may move, to places it may use) would let a
    /// group or account of the request's library see or change more than now, naming who and how; null when none
    /// would, or the question was answered.
    /// </summary>
    public async Task<IResult?> AskAsync(
        IEnumerable<(string From, string To)> moves,
        CancellationToken cancellationToken
    )
    {
        if (!Ruled || _keepAccess is not null)
            return null;
        var gains = new List<AccessGain>();
        foreach (var (from, to) in moves)
        {
            var changes = await _writes.Rules.CompareMoveAsync(PlanOf(from, to), cancellationToken);
            var gained = changes.Where(c => c.Gains).ToList();
            // The library asks about folders only: a file takes the access of where it goes.
            if (gained.Count == 0 || await _open.Storage.GetAsync(from, cancellationToken) is not { IsFolder: true })
                continue;
            foreach (var change in gained)
                gains.Add(await GainOfAsync(from, change, cancellationToken));
        }
        if (gains.Count == 0)
            return null;
        return TypedResults.Problem(
            "More people would see or change the folder there: confirm, keeping their current access or not.",
            statusCode: StatusCodes.Status409Conflict,
            type: AccessGainsType,
            extensions: new Dictionary<string, object?> { ["gains"] = gains }
        );
    }

    /// <summary>
    /// Where a move keeping both names goes: the provider would number a taken name after the rules began to move to
    /// it (and another library's rules there would be taken as left from a folder that is gone), so in a connection
    /// with rules the free name is found first (as the transfers number them) and the move made to it.
    /// </summary>
    public async Task<(string To, RemoteConflict Conflict)> PlaceAsync(
        string from,
        string to,
        RemoteConflict conflict,
        CancellationToken cancellationToken
    )
    {
        if (!Ruled || conflict != RemoteConflict.KeepBoth)
            return (to, conflict);
        var storage = _open.Storage;
        if (await storage.GetAsync(to, cancellationToken) is null)
            return (to, RemoteConflict.Fail);
        var isFolder = await storage.GetAsync(from, cancellationToken) is { IsFolder: true };
        foreach (var name in NumberedNames(RemotePaths.NameOf(to), isFolder))
        {
            var candidate = RemotePaths.Combine(RemotePaths.Parent(to), name);
            if (_open.At(candidate).IsVisible && await storage.GetAsync(candidate, cancellationToken) is null)
                return (candidate, RemoteConflict.Fail);
        }
        return (to, RemoteConflict.Fail);
    }

    /// <summary>
    /// The item moved by <paramref name="provider"/> from <paramref name="from"/> to <paramref name="to"/>, the rules
    /// following it; or why the rules can't follow (<see cref="AccessStatus.Conflict"/>: the request's library has
    /// rules where it goes), the provider not called.
    /// </summary>
    public async Task<(RemoteItem? Item, AccessResult<ConnectionRuleMove>? Refused)> MoveAsync(
        string from,
        string to,
        Func<Task<RemoteItem>> provider,
        CancellationToken cancellationToken
    )
    {
        if (!Ruled)
            return (await provider(), null);
        var plan = PlanOf(from, to);
        var begun = await _writes.Rules.BeginMoveAsync(plan, _keepAccess == true, _state.User?.Id, cancellationToken);
        if (begun.Value is not { } move)
            return (null, begun);
        await using (move)
        {
            var item = await MovedAsync(move, provider);
            await move.CompleteAsync(item, CancellationToken.None);
            if (move.OtherLibraries.Count > 0)
                Warn(plan, item, move.OtherLibraries);
            return (item, null);
        }
    }

    /// <summary>The answer of a route when the rules can't follow (409 <c>folder-rules</c>, or 400).</summary>
    public static IResult AnswerOf(AccessResult<ConnectionRuleMove> refused) =>
        refused.Status == AccessStatus.Invalid
            ? TypedResults.ValidationProblem(refused.Errors!)
            : TypedResults.Problem(
                refused.Problem,
                statusCode: StatusCodes.Status409Conflict,
                type: FileEndpoints.FolderRulesType
            );

    /// <summary>An item's status when the rules can't follow it.</summary>
    public static FileOperationStatus StatusOf(AccessResult<ConnectionRuleMove> refused) =>
        refused.Status == AccessStatus.Invalid ? FileOperationStatus.Invalid : FileOperationStatus.Conflict;

    /// <summary>
    /// The provider's move; a refusal it states (nothing moved) aborts the rules' move. Any other failure leaves both
    /// sets of rules: never less protection, only rules of a folder not found.
    /// </summary>
    private static async Task<RemoteItem> MovedAsync(ConnectionRuleMove move, Func<Task<RemoteItem>> provider)
    {
        try
        {
            return await provider();
        }
        catch (RemoteStorageException error) when (NothingMoved(error.Kind))
        {
            await move.AbortAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary><c>name (2).ext</c>, <c>name (3).ext</c>…, a folder's whole name numbered; a few only (each is a
    /// call to the provider).</summary>
    private static IEnumerable<string> NumberedNames(string name, bool isFolder)
    {
        var extension = isFolder ? string.Empty : Path.GetExtension(name);
        // A name that is only an extension (".profile") has no stem to number.
        var stem = extension.Length == name.Length ? name : name[..^extension.Length];
        if (stem.Length == name.Length)
            extension = string.Empty;
        for (var attempt = 2; attempt <= MaxNumbered; attempt++)
            yield return $"{stem} ({attempt}){extension}";
    }

    private static bool NothingMoved(RemoteErrorKind kind) =>
        kind
            is RemoteErrorKind.NotFound
                or RemoteErrorKind.Exists
                or RemoteErrorKind.InvalidName
                or RemoteErrorKind.Refused
                or RemoteErrorKind.NotSupported
                or RemoteErrorKind.TooLarge
                or RemoteErrorKind.RateLimited;

    private ConnectionMovePlan PlanOf(string from, string to) =>
        new(_open.Connection.Id, _state.TenantId, from, to, _open.Folders.Comparison);

    private async Task<AccessGain> GainOfAsync(
        string folder,
        FolderLevelChange change,
        CancellationToken cancellationToken
    )
    {
        var who =
            change.Principal.GroupId is { } groupId
                ? (await _writes.Accounts.GetGroupsAsync(_state.TenantId, cancellationToken)).FirstOrDefault(g =>
                    g.Id == groupId
                ) switch
                {
                    { BuiltIn: BuiltInGroup.Guests } => "Visitors",
                    { } group => group.Name,
                    null => "A group",
                }
            : await _writes.Accounts.GetUserAsync(change.Principal.UserId!.Value, cancellationToken) is { } user
                ? user.DisplayName ?? user.UserName
            : "An account";
        return new AccessGain(
            folder,
            who,
            change.Principal.GroupId is not null,
            change.Before ?? FolderLevel.Hidden,
            change.After ?? FolderLevel.Hidden
        );
    }

    private void Warn(ConnectionMovePlan plan, RemoteItem item, IReadOnlyList<int> libraries) =>
        _writes
            .Loggers.CreateLogger(typeof(ConnectionMoves))
            .LogSecurityWarning(
                "Connection {ConnectionId}: “{From}” moved to “{To}” by account {UserId} of library {TenantId}; the "
                    + "folder rules of libraries [{Libraries}] followed it without being asked",
                plan.ConnectionId,
                plan.Source,
                item.Path,
                _state.User?.Id,
                plan.TenantId,
                string.Join(", ", libraries)
            );
}
