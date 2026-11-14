namespace Sfe.Core.Files;

/// <summary>What to do when the target folder already holds an item of the same name.</summary>
public enum ConflictChoice
{
    /// <summary>Wait for the person's answer (<see cref="JobBoard.Answer"/>).</summary>
    Ask,

    /// <summary>The item there goes to the trash (needs the right to delete there), this one takes its place.</summary>
    Replace,

    Skip,

    /// <summary>This one gets a free name: <c>name (2).ext</c>.</summary>
    KeepBoth,
}
