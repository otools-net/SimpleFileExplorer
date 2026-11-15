using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <see cref="RemoteIndexSearchTests"/> on SQL Server (LocalDB, when the machine has it; inconclusive otherwise):
/// its collation, its nulls, its LIKE for folders and its translation of the key paging.
/// </summary>
[TestClass]
public sealed class RemoteIndexSearchSqlServerTests : RemoteIndexSearchTests
{
    protected override async Task<CoreServices> CreateServicesAsync(TestLibrary library)
    {
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = $"sfe_test_{Guid.NewGuid():N}",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 60,
        }.ConnectionString;
        if (!OperatingSystem.IsWindows() || !await LocalDbAnswersAsync(connectionString))
            Assert.Inconclusive("SQL Server LocalDB is not on this machine.");
        return await CoreServices.CreateAsync(library, settings =>
        {
            settings["ConnectionStrings:Sfe"] = connectionString;
            settings["Sfe:Database:Provider"] = "SqlServer";
        });
    }

    protected override async Task DropAsync(CoreServices services)
    {
        await using var db = services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContext();
        await db.Database.EnsureDeletedAsync(CancellationToken.None);
    }

    private static async Task<bool> LocalDbAnswersAsync(string connectionString)
    {
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;
        try
        {
            await using var connection = new SqlConnection(master);
            await connection.OpenAsync();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }
}
