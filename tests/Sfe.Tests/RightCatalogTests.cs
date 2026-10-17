using Sfe.Core.Access;
using Sfe.Core.Settings;

namespace Sfe.Tests;

/// <summary>The Read/Write classification of the rights and the patterns naming them.</summary>
[TestClass]
public sealed class RightCatalogTests
{
    [TestMethod]
    public void GuestsNeverGetARightThatChangesSomething()
    {
        var offending = RightCatalog.All.Where(r => r.Kind == RightKind.Write && r.GuestAllowed).Select(r => r.Id).ToList();

        Assert.IsEmpty(offending, string.Join(", ", offending));
    }

    [TestMethod]
    public void OnlyRightsThatChangeSomethingNeedAWritableFolder()
    {
        var offending = RightCatalog.All.Where(r => r.ChangesFiles && r.Kind != RightKind.Write).Select(r => r.Id).ToList();

        Assert.IsEmpty(offending, string.Join(", ", offending));
    }

    [TestMethod]
    public void ServerRightsAreNotAboutFolders()
    {
        var offending = RightCatalog.All.Where(r => r.Scope == RightScope.Server && r.AtFolder is not null).Select(r => r.Id).ToList();

        Assert.IsEmpty(offending, string.Join(", ", offending));
    }

    [TestMethod]
    public void TheRightsThatChangeFilesAreThoseWritingToDisk()
    {
        var changesFiles = RightCatalog.All.Where(r => r.ChangesFiles).Select(r => r.Id).ToList();

        CollectionAssert.AreEquivalent(new[] { Rights.EditImages, Rights.RemoveDuplicates, Rights.CreateFolders, Rights.RenameFiles }, changesFiles);
    }

    [TestMethod]
    public void TaggingNeedsAReadableFolderOnly()
    {
        var assign = RightCatalog.Find(Rights.AssignTags)!;

        Assert.AreEqual(RightKind.Write, assign.Kind);
        Assert.AreEqual(FolderLevel.Read, assign.AtFolder);
    }

    [TestMethod]
    [DataRow("kind:write", Rights.AssignTags, true)]
    [DataRow("kind:write", Rights.ServerSettings, true)]
    [DataRow("kind:write", Rights.Browse, false)]
    [DataRow("kind:write", Rights.ViewLogs, false)]
    [DataRow("KIND:WRITE ", Rights.EditImages, true)]
    [DataRow("folder:write", Rights.EditImages, true)]
    [DataRow("folder:write", Rights.RemoveDuplicates, true)]
    [DataRow("folder:write", Rights.AssignTags, false)]
    [DataRow("folder:write", Rights.ManageMembers, false)]
    [DataRow("kind:write", "no.such.right", false)]
    [DataRow("files.*", Rights.EditImages, true)]
    [DataRow("files.*", Rights.Browse, false)]
    public void PatternsNameRightsByIdOrByKind(string pattern, string right, bool expected) =>
        Assert.AreEqual(expected, RightCatalog.Matches(pattern, right));

    [TestMethod]
    public void SettingKeysAreNamedByTheirTextOnly()
    {
        var key = SettingCatalog.All[0].Key;

        Assert.IsFalse(SettingCatalog.All.Any(s => SettingCatalog.Matches(RightCatalog.WriteSelector, s.Key) || SettingCatalog.Matches(RightCatalog.FileWriteSelector, s.Key)));
        Assert.IsTrue(SettingCatalog.Matches(key[..(key.IndexOf('.') + 1)] + "*", key));
    }
}
