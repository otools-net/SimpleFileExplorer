using Sfe.Core.Access;
using Sfe.Core.Paths;

namespace Sfe.Tests;

/// <summary>How folder rules decide what a subject may do where (<see cref="FolderPolicy"/>), without a database.</summary>
[TestClass]
public sealed class FolderPolicyTests
{
    private static readonly HashSet<string> Everything = RightCatalog.TenantRights.Select(r => r.Id).ToHashSet();

    [TestMethod]
    public void WithoutRulesEveryRightAppliesEverywhere()
    {
        var policy = FolderPolicy.Compile([Group([Rights.Browse, Rights.EditImages]), Account()]);
        var access = policy.At("Photos/2024", Granted(Rights.Browse, Rights.EditImages));

        Assert.IsTrue(policy.IsUnrestricted);
        Assert.IsTrue(access.Has(Rights.EditImages));
        Assert.IsFalse(access.PathOnly);
        Assert.IsTrue(policy.FilterFor(Rights.Browse, Granted(Rights.Browse)).IsEverything);
    }

    [TestMethod]
    public void TheNearestRuleAboveAPathWins()
    {
        var policy = FolderPolicy.Compile([Group([Rights.Browse], ("Private", FolderLevel.Hidden), ("Private/Shared", FolderLevel.Read))]);
        var granted = Granted(Rights.Browse);

        Assert.IsTrue(policy.At("", granted).Has(Rights.Browse));
        Assert.IsTrue(policy.At("Public/a.jpg", granted).Has(Rights.Browse));
        Assert.IsFalse(policy.At("Private", granted).Has(Rights.Browse));
        Assert.IsFalse(policy.At("Private/Other/b.jpg", granted).Has(Rights.Browse));
        Assert.IsTrue(policy.At("Private/Shared", granted).Has(Rights.Browse));
        Assert.IsTrue(policy.At("Private/Shared/Deep/c.jpg", granted).Has(Rights.Browse));
        Assert.IsTrue(policy.At("Private Stuff", granted).Has(Rights.Browse), "A sibling with the same start is not below the rule");
    }

    [TestMethod]
    public void GroupsAddUp()
    {
        var kids = Group([Rights.Browse], ("Adult", FolderLevel.Hidden));
        var family = Group([Rights.Browse]);

        Assert.IsFalse(FolderPolicy.Compile([kids]).At("Adult", Granted(Rights.Browse)).Has(Rights.Browse));
        Assert.IsTrue(FolderPolicy.Compile([kids, family]).At("Adult", Granted(Rights.Browse)).Has(Rights.Browse),
            "Another group of the account that sees the folder shows it");
    }

    [TestMethod]
    public void RightsStayWithTheGroupThatGrantsThem()
    {
        // Editors may edit pictures but only read in Archive; Browsers may write in Archive but have no editing right.
        var editors = Group([Rights.Browse, Rights.EditImages], ("Archive", FolderLevel.Read));
        var browsers = Group([Rights.Browse], ("Archive", FolderLevel.Write));
        var policy = FolderPolicy.Compile([editors, browsers]);
        var granted = Granted(Rights.Browse, Rights.EditImages);

        Assert.IsFalse(policy.At("Archive/a.jpg", granted).Has(Rights.EditImages), "Neither group lets anyone edit in Archive");
        Assert.IsTrue(policy.At("Archive/a.jpg", granted).Has(Rights.Browse));
        Assert.IsTrue(policy.At("Inbox/a.jpg", granted).Has(Rights.EditImages));
    }

    [TestMethod]
    public void AnAccountRuleOnlyAddsToItsGroups()
    {
        var users = Group([Rights.Browse, Rights.EditImages], ("Private", FolderLevel.Hidden), ("Shared", FolderLevel.Read));
        var granted = Granted(Rights.Browse, Rights.EditImages);

        var opened = FolderPolicy.Compile([users, Account([Rights.Browse, Rights.EditImages], ("Private/Mine", FolderLevel.Write), ("Shared", FolderLevel.Hidden))]);

        Assert.IsTrue(opened.At("Private/Mine/a.jpg", granted).Has(Rights.EditImages), "The account's own rule opens its folder");
        Assert.IsFalse(opened.At("Private/Other", granted).Has(Rights.Browse));
        Assert.IsTrue(opened.At("Shared/b.jpg", granted).Has(Rights.Browse), "Hidden for the account adds nothing but takes nothing either");
        Assert.IsTrue(opened.At("Elsewhere", granted).Has(Rights.EditImages), "Without a rule above, the account has no say: its groups decide");
    }

    [TestMethod]
    public void RightsOfTheWholeSpaceApplyInHiddenFolders()
    {
        var policy = FolderPolicy.Compile([Group([Rights.Browse, Rights.InterfaceSettings, Rights.ManageTags], ("", FolderLevel.Hidden))]);
        var access = policy.At("Anything", Granted(Rights.Browse, Rights.InterfaceSettings, Rights.ManageTags));

        Assert.IsFalse(access.Has(Rights.Browse));
        Assert.IsTrue(access.Has(Rights.InterfaceSettings));
        Assert.IsTrue(access.Has(Rights.ManageTags));
    }

    [TestMethod]
    public void TaggingNeedsAReadableFolderAndEditingAWritableOne()
    {
        var policy = FolderPolicy.Compile([Group([Rights.Browse, Rights.AssignTags, Rights.EditImages], ("Archive", FolderLevel.Read))]);
        var access = policy.At("Archive/a.jpg", Granted(Rights.Browse, Rights.AssignTags, Rights.EditImages));

        Assert.IsTrue(access.Has(Rights.AssignTags));
        Assert.IsFalse(access.Has(Rights.EditImages));
        CollectionAssert.AreEquivalent(new[] { Rights.Browse, Rights.AssignTags }, access.Allowed.ToList());
    }

    [TestMethod]
    public void RightsTheLibraryDoesNotGrantStayOff()
    {
        var policy = FolderPolicy.Compile([Group([Rights.Browse, Rights.EditImages], ("Inbox", FolderLevel.Write))]);

        Assert.IsFalse(policy.At("Inbox", Granted(Rights.Browse)).Has(Rights.EditImages), "Switched off by the server: no folder gives it back");
        Assert.IsTrue(policy.FilterFor(Rights.EditImages, Granted(Rights.Browse)).IsNothing);
    }

    [TestMethod]
    public void AHiddenFolderLeadsToWhatIsAllowedBelowIt()
    {
        var policy = FolderPolicy.Compile([Group([Rights.Browse], ("Private", FolderLevel.Hidden), ("Private/Family/Shared", FolderLevel.Read), ("Media", FolderLevel.Read))]);
        var granted = Granted(Rights.Browse);

        Assert.IsTrue(policy.At("Private", granted).PathOnly);
        Assert.IsTrue(policy.At("Private/Family", granted).PathOnly, "A folder without a rule of its own leads there too");
        Assert.IsTrue(policy.At("Private/Family", granted).IsVisible);
        Assert.IsFalse(policy.At("Private/Other", granted).IsVisible);
        Assert.IsFalse(policy.At("Private/Family/Shared", granted).PathOnly);

        var children = policy.ChildrenWithRules("", granted);
        CollectionAssert.AreEquivalent(new[] { "Private", "Media" }, children.Keys.ToList());
        Assert.IsTrue(children["Private"].PathOnly);
        CollectionAssert.AreEquivalent(new[] { "Family" }, policy.ChildrenWithRules("Private", granted).Keys.ToList());
        Assert.IsEmpty(policy.ChildrenWithRules("Media", granted));
    }

    [TestMethod]
    public void ATreeChangesOnlyWhereEverythingInItAllowsIt()
    {
        var policy = FolderPolicy.Compile([Group([Rights.Browse, Rights.EditImages], ("Photos/Kept", FolderLevel.Read), ("Videos/Hidden", FolderLevel.Hidden))]);
        var granted = Granted(Rights.Browse, Rights.EditImages);

        Assert.IsFalse(policy.AllowsThroughout("Photos", Rights.EditImages, granted), "A read-only folder below");
        Assert.IsFalse(policy.AllowsThroughout("Videos", Rights.EditImages, granted), "A hidden folder below");
        Assert.IsTrue(policy.AllowsThroughout("Photos/Other", Rights.EditImages, granted));
        Assert.IsFalse(policy.AllowsThroughout("Videos", Rights.Browse, granted), "Nobody changes what they can't see");
    }

    [TestMethod]
    public void PathsCompareAsTheFileSystemDoes()
    {
        var policy = FolderPolicy.Compile([Group([Rights.Browse], ("Private", FolderLevel.Hidden))]);

        Assert.AreEqual(!LibraryPaths.IsCaseInsensitiveFileSystem, policy.At("private/a.jpg", Granted(Rights.Browse)).Has(Rights.Browse));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public void TheFilterForTheIndexMatchesThePolicy(int seed)
    {
        var random = new Random(seed);
        var folders = RandomTree(random);
        var principals = Enumerable.Range(0, 3)
            .Select(_ => Group([Rights.Browse], RandomRules(random, folders)))
            .Append(Account([Rights.Browse], RandomRules(random, folders)))
            .ToList();
        var policy = FolderPolicy.Compile(principals);
        var granted = Granted(Rights.Browse);
        var filter = policy.FilterFor(Rights.Browse, granted);

        foreach (var path in folders.Concat(folders.Select(f => f.Length == 0 ? "file.jpg" : f + "/file.jpg")))
            Assert.AreEqual(policy.At(path, granted).Has(Rights.Browse), filter.Includes(path), $"{path} (seed {seed})");
    }

    internal static List<string> RandomTree(Random random)
    {
        var folders = new List<string> { "" };
        string[] names = ["A", "B", "Ab", "a b", "C"];
        for (var i = 0; i < 30; i++)
        {
            var parent = folders[random.Next(folders.Count)];
            var child = names[random.Next(names.Length)];
            var path = parent.Length == 0 ? child : parent + "/" + child;
            if (path.Split('/').Length <= 4 && !folders.Contains(path, LibraryPaths.PathComparer))
                folders.Add(path);
        }
        return folders;
    }

    internal static (string, FolderLevel)[] RandomRules(Random random, List<string> folders) =>
        [.. Enumerable.Range(0, random.Next(0, 6))
            .Select(_ => (folders[random.Next(folders.Count)], (FolderLevel)random.Next(3)))
            .DistinctBy(r => r.Item1, LibraryPaths.PathComparer)];

    private static FolderPrincipal Group(string[] rights, params (string Path, FolderLevel Level)[] rules) =>
        new(rules.ToDictionary(r => r.Path, r => r.Level), rights.ToHashSet(), true);

    private static FolderPrincipal Account(string[]? rights = null, params (string Path, FolderLevel Level)[] rules) =>
        new(rules.ToDictionary(r => r.Path, r => r.Level), (rights ?? []).ToHashSet(), false);

    private static HashSet<string> Granted(params string[] rights) => rights.Where(Everything.Contains).ToHashSet();
}
