namespace Otools.Net.Sfe.Core.Tasks;

/// <summary>
/// The account whose request runs in this async flow, set by the web host for each request: a run begun with
/// <see cref="TaskTrigger.Manual"/> from it (or from a task it starts, which inherits the value) remembers who started
/// it, so that its end can be told to that account (<see cref="TaskEndings"/>). Kept in memory only: nothing is
/// stored with the run.
/// </summary>
public static class TaskStarter
{
    private static readonly AsyncLocal<int?> Account = new();

    /// <summary>The account of the request this flow serves; null for a visitor or outside a request.</summary>
    public static int? Current
    {
        get => Account.Value;
        set => Account.Value = value;
    }
}
