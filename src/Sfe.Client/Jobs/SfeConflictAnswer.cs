namespace Otools.Net.Sfe.Client;

/// <summary>The answer to a job's name clash.</summary>
public sealed class SfeConflictAnswer
{
    /// <summary>An answer of <paramref name="choice"/>, for this clash or for the next ones too.</summary>
    /// <param name="choice">What to do; <see cref="SfeConflictChoice.Ask"/> isn't an answer.</param>
    /// <param name="forAll">Whether the job answers the next clashes the same way without asking.</param>
    public SfeConflictAnswer(SfeConflictChoice choice, bool forAll = false)
    {
        Choice = choice;
        ForAll = forAll;
    }

    /// <summary>What to do.</summary>
    public SfeConflictChoice Choice { get; }

    /// <summary>Whether the next clashes get the same answer without asking.</summary>
    public bool ForAll { get; }
}
