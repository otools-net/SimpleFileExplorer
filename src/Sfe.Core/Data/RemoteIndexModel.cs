using Microsoft.EntityFrameworkCore;

namespace Otools.Net.Sfe.Core.Data;

/// <summary>
/// The tables of the connections' index in a library's database (<see cref="RemoteDirectory"/>,
/// <see cref="RemoteFile"/>, <see cref="RemoteFileTag"/>, <see cref="RemoteIndexState"/>): the shapes measured in
/// phase 0 of agents/docs/development/connections-index-plan.md. Every index leads with the connection, so no query
/// reads another connection's rows. New since 1.86: an existing SQLite index gets them from
/// <see cref="DatabaseSchema"/>.
/// </summary>
internal static class RemoteIndexModel
{
    /// <summary>The provider's ids kept; a longer one is not kept (the file is then known by its path only).</summary>
    public const int MaxProviderIdLength = 200;

    /// <summary>The content hashes kept; a longer one is not kept.</summary>
    public const int MaxHashLength = 128;

    public static void Configure(ModelBuilder modelBuilder, bool isSqlServer)
    {
        ConfigureDirectories(modelBuilder);
        ConfigureFiles(modelBuilder, isSqlServer);
        ConfigureTags(modelBuilder);
        modelBuilder.Entity<RemoteIndexState>(entity =>
        {
            entity.ToTable("RemoteIndexes");
            entity.HasKey(s => s.ConnectionId);
            entity.Property(s => s.ConnectionId).ValueGeneratedNever();
            entity.Property(s => s.RootPath).IsRequired();
            entity.Property(s => s.AccountKey).HasMaxLength(64).IsUnicode(false);
            entity.Property(s => s.Phase).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
            entity.Property(s => s.LastError).HasMaxLength(1000);
        });
    }

    private static void ConfigureDirectories(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<RemoteDirectory>(entity =>
        {
            entity.ToTable("RemoteDirectories");
            entity.Property(d => d.FullName).HasMaxLength(SfeDbContext.MaxPathLength).IsRequired();
            entity.Property(d => d.DisplayPath).HasMaxLength(SfeDbContext.MaxPathLength).IsRequired();
            entity.Property(d => d.Name).HasMaxLength(SfeDbContext.MaxPathLength).IsRequired();
            entity.Property(d => d.ProviderId).HasMaxLength(MaxProviderIdLength);
            entity.HasIndex(d => new { d.ConnectionId, d.FullName }).IsUnique();
            entity.HasIndex(d => new { d.ConnectionId, d.ProviderId });
            // The next folder a listing goes to, and what a sync drops when it ends.
            entity.HasIndex(d => new { d.ConnectionId, d.Pending });
            entity.HasIndex(d => new { d.ConnectionId, d.Removed });
            // A folder's rows are deleted before their parent's (RemoteIndexCleanup): one cascade path on SQL Server.
            entity
                .HasOne(d => d.Parent)
                .WithMany(d => d.Directories)
                .HasForeignKey(d => d.ParentId)
                .OnDelete(DeleteBehavior.NoAction);
        });

    private static void ConfigureFiles(ModelBuilder modelBuilder, bool isSqlServer) =>
        modelBuilder.Entity<RemoteFile>(entity =>
        {
            entity.ToTable("RemoteFiles");
            entity.Property(f => f.FullName).HasMaxLength(SfeDbContext.MaxPathLength).IsRequired();
            entity.Property(f => f.DisplayPath).HasMaxLength(SfeDbContext.MaxPathLength).IsRequired();
            entity.Property(f => f.Name).HasMaxLength(SfeDbContext.MaxPathLength).IsRequired();
            entity.Property(f => f.Extension).HasMaxLength(SfeDbContext.MaxPathLength);
            entity.Property(f => f.ProviderId).HasMaxLength(MaxProviderIdLength);
            entity.Property(f => f.ContentHash).HasMaxLength(MaxHashLength).IsUnicode(false);
            entity
                .Property(f => f.Pixels)
                .HasComputedColumnSql(
                    isSqlServer ? "CAST([Width] AS bigint) * [Height]" : "\"Width\" * \"Height\"",
                    stored: false
                );
            if (!isSqlServer)
            {
                // As the library's Files: names and extensions match whatever their case.
                entity.Property(f => f.Name).UseCollation("NOCASE");
                entity.Property(f => f.Extension).UseCollation("NOCASE");
            }
            entity.HasIndex(f => new { f.ConnectionId, f.FullName }).IsUnique();
            // Unique where set (SQL Server filters out the nulls by itself).
            entity.HasIndex(f => new { f.ConnectionId, f.ProviderId }).IsUnique();
            entity.HasIndex(f => new { f.ConnectionId, f.DirectoryId });
            entity.HasIndex(f => new { f.ConnectionId, f.Name });
            entity.HasIndex(f => new { f.ConnectionId, f.Extension });
            entity.HasIndex(f => new { f.ConnectionId, f.Size });
            entity.HasIndex(f => new { f.ConnectionId, f.LastWriteTime });
            entity.HasIndex(f => new { f.ConnectionId, f.Duration });
            entity.HasIndex(f => new { f.ConnectionId, f.Pixels });
            entity.HasIndex(f => new { f.ConnectionId, f.Removed });
            entity
                .HasOne(f => f.Directory)
                .WithMany(d => d.Files)
                .HasForeignKey(f => f.DirectoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    private static void ConfigureTags(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<RemoteFileTag>(entity =>
        {
            entity.ToTable("RemoteFileTags");
            entity.HasKey(l => new { l.FileId, l.TagId });
            entity.HasIndex(l => l.TagId);
            entity
                .HasOne(l => l.File)
                .WithMany(f => f.TagLinks)
                .HasForeignKey(l => l.FileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(l => l.Tag).WithMany().HasForeignKey(l => l.TagId).OnDelete(DeleteBehavior.Cascade);
        });
}
