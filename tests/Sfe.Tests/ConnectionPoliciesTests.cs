using Sfe.Core.Access;
using Sfe.Core.Connections;

namespace Sfe.Tests;

/// <summary>
/// <see cref="ConnectionPolicies.Compile"/> and <see cref="ConnectionFolders"/>: each group's rights only at the
/// levels it has (its row's, or its rules'), nearest rule wins, the account's rules only add, read only caps, case
/// as the provider compares names, and what a listing of a folder leaves out.
/// </summary>
[TestClass]
public sealed class ConnectionPoliciesTests
{
    private const int Deleters = 1;
    private const int Writers = 2;

    private static readonly HashSet<string> DeleteRights = [Rights.Browse, Rights.ViewFiles, Rights.DeleteFiles];

    private static readonly HashSet<string> WriteRights =
    [
        Rights.Browse,
        Rights.ViewFiles,
        Rights.RenameFiles,
        Rights.CreateFolders,
    ];

    [TestMethod]
    public void ARightOfOneGroupNeverAddsToTheLevelOfAnother()
    {
        var input = new ConnectionPolicyInput(
            [new(Deleters, FolderLevel.Read, DeleteRights), new(Writers, FolderLevel.Write, WriteRights)],
            []
        );

        var folders = FoldersOf(input);

        Assert.IsFalse(folders.Allows("Docs/a.txt", Rights.DeleteFiles), "delete is granted at Read only");
        Assert.IsTrue(folders.Allows("Docs/a.txt", Rights.RenameFiles));
        Assert.IsFalse(folders.AllowsSomewhere(Rights.DeleteFiles));
    }

    [TestMethod]
    public void ARuleRaisesOrLowersItsGroupAndTheNearestWins()
    {
        var input = new ConnectionPolicyInput(
            [new(Deleters, FolderLevel.Read, DeleteRights)],
            [
                new(Deleters, "Docs", FolderLevel.Write),
                new(Deleters, "Docs/Private", FolderLevel.Hidden),
                new(Deleters, "Docs/Private/Open", FolderLevel.Read),
            ]
        );

        var folders = FoldersOf(input);

        Assert.IsTrue(folders.Allows("Docs/a.txt", Rights.DeleteFiles), "raised above its row's level");
        Assert.IsFalse(folders.Allows("Other/a.txt", Rights.DeleteFiles));
        Assert.IsFalse(folders.Sees("Docs/Private/x.txt"));
        Assert.IsTrue(folders.At("Docs/Private").PathOnly);
        Assert.IsTrue(folders.Sees("Docs/Private/Open/y.txt"));
        Assert.IsFalse(folders.AllowsThroughout("Docs", Rights.DeleteFiles), "a hidden folder inside");
    }

    [TestMethod]
    public void TheAccountsRulesOnlyAddAndNoGroupMeansNothing()
    {
        var input = new ConnectionPolicyInput(
            [new(Writers, FolderLevel.Read, WriteRights)],
            [new(null, "Mine", FolderLevel.Write), new(Writers, "Shared", FolderLevel.Hidden)]
        );

        var folders = FoldersOf(input);
        var none = FoldersOf(new ConnectionPolicyInput([], [new(null, "Mine", FolderLevel.Write)]));

        Assert.IsTrue(folders.Allows("Mine/a.txt", Rights.RenameFiles));
        Assert.IsFalse(folders.Allows("Other/a.txt", Rights.RenameFiles));
        Assert.IsFalse(folders.Sees("Shared/a.txt"));
        Assert.IsFalse(none.Sees(""), "the groups' rows are the gate");
        Assert.IsFalse(none.Sees("Mine/a.txt"));
    }

    [TestMethod]
    public void ReadOnlyCapsEveryLevelAtRead()
    {
        var input = new ConnectionPolicyInput(
            [new(Writers, FolderLevel.Write, WriteRights)],
            [new(Writers, "Docs", FolderLevel.Write)]
        );

        var capped = FoldersOf(input, FolderLevel.Read);
        var owner = new ConnectionFolders(
            ConnectionPolicies.Whole(FolderLevel.Read, StringComparison.Ordinal),
            WriteRights,
            StringComparison.Ordinal
        );

        Assert.IsTrue(capped.Sees("Docs/a.txt"));
        Assert.IsFalse(capped.AllowsSomewhere(Rights.RenameFiles));
        Assert.IsFalse(owner.AllowsSomewhere(Rights.CreateFolders));
        Assert.IsTrue(owner.Allows("x", Rights.ViewFiles));
        Assert.IsTrue(ConnectionPolicies.Whole(FolderLevel.Write, StringComparison.Ordinal).IsUnrestricted);
    }

    [TestMethod]
    public void PathsCompareAsTheProviderComparesNames()
    {
        var input = new ConnectionPolicyInput(
            [new(Writers, FolderLevel.Write, WriteRights)],
            [new(Writers, "Private", FolderLevel.Hidden), new(Writers, "PRIVATE", FolderLevel.Read)]
        );

        var folding = FoldersOf(input, comparison: StringComparison.OrdinalIgnoreCase);
        var exact = FoldersOf(input, comparison: StringComparison.Ordinal);

        Assert.IsFalse(folding.Sees("private/a.txt"), "two rows for one path count as the stricter");
        Assert.IsFalse(folding.Sees("PrIvAtE"));
        Assert.IsFalse(exact.Sees("Private/a.txt"));
        Assert.IsTrue(exact.Sees("private/a.txt"));
    }

    [TestMethod]
    public void AListingLeavesOutHiddenChildrenAndAPathOnlyFolderShowsWhatLeadsOn()
    {
        var input = new ConnectionPolicyInput(
            [new(Writers, FolderLevel.Write, WriteRights)],
            [
                new(Writers, "Private", FolderLevel.Hidden),
                new(Writers, "Lead", FolderLevel.Hidden),
                new(Writers, "Lead/Open", FolderLevel.Read),
            ]
        );
        var folders = FoldersOf(input, comparison: StringComparison.OrdinalIgnoreCase);

        var root = folders.ListingFilterOf("", folders.At(""))!;
        var lead = folders.ListingFilterOf("Lead", folders.At("Lead"))!;
        var view = folders.ViewOf("", folders.At(""))!;

        Assert.IsFalse(root.Shows("private"));
        Assert.IsTrue(root.Shows("Lead"));
        Assert.IsTrue(root.Shows("Public"));
        Assert.IsTrue(lead.Shows("open"));
        Assert.IsFalse(lead.Shows("other.txt"));
        Assert.IsFalse(view.PathOnly);
        CollectionAssert.AreEquivalent(new[] { "Lead" }, view.Children!.Keys.ToArray());
        Assert.IsTrue(view.Children["lead"].PathOnly);
        Assert.IsNull(
            new ConnectionFolders(FolderPolicy.Unrestricted, WriteRights, StringComparison.Ordinal).ViewOf(
                "",
                FolderPolicy.Unrestricted.At("", WriteRights)
            )
        );
    }

    [TestMethod]
    public void ARuleAppliesToItsFolderFoundByIdAtAnotherPathTheStricterWinning()
    {
        var input = new ConnectionPolicyInput(
            [new(Writers, FolderLevel.Write, WriteRights)],
            [
                new(Writers, "Old/Plans", FolderLevel.Hidden, "id:plans"),
                new(Writers, "Docs", FolderLevel.Read, "id:docs"),
                new(Writers, "Shared", FolderLevel.Write),
            ]
        );
        var comparison = StringComparison.Ordinal;
        var granted = DeleteRights.Union(WriteRights).ToHashSet();
        var folders = new ConnectionFolders(
            ConnectionPolicies.Compile(input, FolderLevel.Write, comparison),
            granted,
            comparison,
            ConnectionPolicy.PlacesOf(input.Rules)
        );
        RemoteItem[] items =
        [
            new("Plans", "Public/Plans", true, Id: "id:plans"),
            new("Papers", "Shared/Papers", true, Id: "id:docs"),
            new("Other", "Public/Other", true, Id: "id:other"),
        ];
        var listing = new RemoteListing(items, 0, 3, 3, true, false);

        Assert.IsTrue(folders.Sees("Public/Plans"), "by its path alone");
        Assert.IsFalse(folders.At("Public/Plans", "id:plans").IsVisible, "the rule set on it before it moved");
        Assert.IsTrue(folders.At("Shared/Papers", null).Has(Rights.RenameFiles));
        Assert.IsFalse(folders.At("Shared/Papers", "id:docs").Has(Rights.RenameFiles), "the stricter of both");
        Assert.IsTrue(folders.At("Shared/Papers", "id:docs").Has(Rights.ViewFiles));
        Assert.IsTrue(folders.At("Docs", "id:docs").Has(Rights.ViewFiles), "where the rule is");
        var left = folders.WithoutHiddenByIds(listing);
        CollectionAssert.AreEqual(new[] { "Papers", "Other" }, left.Items.Select(i => i.Name).ToArray());
        Assert.AreEqual((2, 2), (left.Total, left.DirectoryCount));
        var view = folders.ViewOf("Shared", folders.At("Shared"), left.Items)!;
        CollectionAssert.DoesNotContain(view.Children!["Papers"].Rights.ToArray(), Rights.RenameFiles);
    }

    private static ConnectionFolders FoldersOf(
        ConnectionPolicyInput input,
        FolderLevel cap = FolderLevel.Write,
        StringComparison comparison = StringComparison.Ordinal
    )
    {
        var granted = DeleteRights.Union(WriteRights).ToHashSet();
        return new ConnectionFolders(ConnectionPolicies.Compile(input, cap, comparison), granted, comparison);
    }
}
