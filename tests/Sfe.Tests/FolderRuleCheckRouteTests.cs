using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <c>PUT access/folder-rules?validateOnly=true</c>, the checks the share-a-folder wizard runs on Next: refused as the
/// save would be, field by field, and nothing changed.
/// </summary>
[TestClass]
public sealed class FolderRuleCheckRouteTests
{
    private const string CheckUrl = $"{V1}/access/folder-rules?validateOnly=true";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private int _tenant;
    private int _users;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        Directory.CreateDirectory(_library.FullPath("Media"));
        _factory = ApiClients.CreateFactory(_library);
        _admin = await ApiClients.SetUpAsync(_factory, Token);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        _tenant = (await accounts.GetTenantsAsync(Token)).Single(t => t.IsDefault).Id;
        _users = (await accounts.GetBuiltInGroupAsync(_tenant, BuiltInGroup.Users, Token))!.Id;
    }

    [TestCleanup]
    public void Cleanup()
    {
        _admin.Dispose();
        _factory.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ARuleThatWouldBeSetIsCheckedWithoutBeingSet()
    {
        using var checkedRule = await PutAsync(new FolderRuleChange("Media", _users, null, FolderLevel.Read));

        Assert.AreEqual(HttpStatusCode.NoContent, checkedRule.StatusCode);
        Assert.IsEmpty(await RulesAsync(), "Nothing is saved");
    }

    [TestMethod]
    public async Task EachRefusalNamesItsField()
    {
        var badPath = await ErrorsAsync(new FolderRuleChange("../Elsewhere", _users, null, FolderLevel.Read));
        var nobody = await ErrorsAsync(new FolderRuleChange("Media", null, null, null));
        var noSuchGroup = await ErrorsAsync(new FolderRuleChange("Media", 9999, null, FolderLevel.Read));
        var stranger = await ErrorsAsync(new FolderRuleChange("Media", null, 9999, FolderLevel.Read));

        CollectionAssert.AreEqual(new[] { "path" }, badPath.Keys.ToArray(), "The folder comes first");
        CollectionAssert.AreEqual(
            new[] { "userId" },
            nobody.Keys.ToArray(),
            "Who, before a level is picked: the wizard's first step goes on past it"
        );
        CollectionAssert.AreEqual(new[] { "groupId" }, noSuchGroup.Keys.ToArray());
        CollectionAssert.AreEqual(new[] { "userId" }, stranger.Keys.ToArray());
    }

    [TestMethod]
    public async Task OneRuleTooManyIsRefusedBeforeTheSave()
    {
        await using (var db = await DbAsync())
        {
            db.FolderRules.AddRange(
                Enumerable
                    .Range(0, FolderRuleService.MaxRules)
                    .Select(i => new FolderRule
                    {
                        TenantId = _tenant,
                        Path = $"Folder {i}",
                        GroupId = _users,
                        Level = FolderLevel.Read,
                        Created = DateTime.UtcNow,
                    })
            );
            await db.SaveChangesAsync(Token);
        }

        using var added = await PutAsync(new FolderRuleChange("Media", _users, null, FolderLevel.Read));
        using var changed = await PutAsync(new FolderRuleChange("Folder 3", _users, null, FolderLevel.Hidden));

        Assert.AreEqual(HttpStatusCode.Conflict, added.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, changed.StatusCode, "Changing a rule is still possible");
    }

    private Task<HttpResponseMessage> PutAsync(FolderRuleChange change) =>
        _admin.PutAsJsonAsync(CheckUrl, change, Json, Token);

    private async Task<IDictionary<string, string[]>> ErrorsAsync(FolderRuleChange change)
    {
        using var response = await PutAsync(change);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Token))!.Errors;
    }

    private async Task<List<FolderRuleView>> RulesAsync() =>
        (await _admin.GetFromJsonAsync<List<FolderRuleView>>($"{V1}/access/folder-rules", Json, Token))!;

    private async Task<AccessDbContext> DbAsync() =>
        await _factory.Services.GetRequiredService<IDbContextFactory<AccessDbContext>>().CreateDbContextAsync(Token);
}
