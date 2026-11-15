namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary><see cref="SfePath"/>: the API's paths put together and taken apart, no disk involved.</summary>
[TestClass]
public sealed class SfePathTests
{
    [TestMethod]
    [DataRow(null, "")]
    [DataRow("", "")]
    [DataRow("/", "")]
    [DataRow("/Photos//2026/", "Photos/2026")]
    [DataRow(@"a\b", @"a\b")]
    public void APathIsNormalizedWithoutEmptyNames(string? path, string expected) =>
        Assert.AreEqual(expected, SfePath.Normalize(path));

    [TestMethod]
    public void PathsAndNamesAreCombinedWithSlashes()
    {
        Assert.AreEqual("Photos/2026/a.jpg", SfePath.Combine("Photos/", "", null, "/2026", "a.jpg"));
        Assert.AreEqual("", SfePath.Combine());
    }

    [TestMethod]
    [DataRow("Photos/2026/a.jpg", "Photos/2026", "a.jpg")]
    [DataRow("a.jpg", "", "a.jpg")]
    [DataRow("", "", "")]
    public void APathHasAParentAndAName(string path, string parent, string name)
    {
        Assert.AreEqual(parent, SfePath.Parent(path));
        Assert.AreEqual(name, SfePath.Name(path));
    }
}
