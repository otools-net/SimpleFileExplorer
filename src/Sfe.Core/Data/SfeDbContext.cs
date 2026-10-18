using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sfe.Core.Paths;
using Sfe.Core.Tagging;

namespace Sfe.Core.Data;

/// <summary>
/// File index: indexed folders and files, tags, and tagged files no longer found. On SQL Server the tables live in the
/// <c>SFE</c> schema.
/// </summary>
public class SfeDbContext(DbContextOptions<SfeDbContext> options) : DbContext(options)
{
    public const string Schema = "SFE";
    public const int MaxPathLength = 260;
    public const int MaxTagNameLength = 200;

    public DbSet<DirectoryEntry> Directories => Set<DirectoryEntry>();
    public DbSet<FileEntry> Files => Set<FileEntry>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TagActivity> TagActivity => Set<TagActivity>();
    public DbSet<TagAlias> TagAliases => Set<TagAlias>();
    public DbSet<TagParent> TagParents => Set<TagParent>();
    public DbSet<FileTag> FileTags => Set<FileTag>();
    public DbSet<MissingFile> MissingFiles => Set<MissingFile>();
    public DbSet<MissingFileTag> MissingFileTags => Set<MissingFileTag>();
    public DbSet<TaskSchedule> TaskSchedules => Set<TaskSchedule>();
    public DbSet<TaskRun> TaskRuns => Set<TaskRun>();
    public DbSet<TrashEntry> TrashEntries => Set<TrashEntry>();
    public DbSet<TrashTag> TrashTags => Set<TrashTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var isSqlServer = Database.IsSqlServer();
        if (isSqlServer)
            modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<DirectoryEntry>(entity =>
        {
            entity.ToTable("Directories");
            ConfigureEntry(entity, isSqlServer);
            entity.HasOne(d => d.Parent)
                .WithMany(d => d.Directories)
                .HasForeignKey(d => d.ParentId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<FileEntry>(entity =>
        {
            entity.ToTable("Files");
            ConfigureEntry(entity, isSqlServer);
            entity.Property(f => f.Sha1).HasMaxLength(40).IsUnicode(false);
            entity.HasIndex(f => f.Sha1);
            // Duplicate groups (DuplicateFinder): grouping by size and fingerprint, biggest first, reads this index
            // alone. An existing SQLite index gets it from DatabaseInitializer.
            entity.HasIndex(f => new { f.Size, f.Sha1 });
            // "e:mp4" searches. SQL Server cannot index NVARCHAR(MAX); an extension is never longer than the path it
            // ends. An existing SQLite index gets the index from DatabaseInitializer.
            entity.Property(f => f.Extension).HasMaxLength(MaxPathLength);
            entity.HasIndex(f => f.Extension);
            // Search results sorted by date (FileSearchService). An existing SQLite index gets it from DatabaseInitializer.
            entity.HasIndex(f => f.LastWriteTime);
            // Search results sorted by duration or resolution. Virtual (not stored) because SQLite can only add a
            // virtual generated column to an existing table (DatabaseInitializer); bigint so that no odd header
            // overflows an int on SQL Server.
            entity.HasIndex(f => f.Duration);
            entity.Property(f => f.Pixels).HasComputedColumnSql(
                isSqlServer ? "CAST([Width] AS bigint) * [Height]" : "\"Width\" * \"Height\"", stored: false);
            entity.HasIndex(f => f.Pixels);
            entity.HasOne(f => f.Directory)
                .WithMany(d => d.Files)
                .HasForeignKey(f => f.DirectoryId)
                .OnDelete(DeleteBehavior.Cascade);
            if (!isSqlServer)
                entity.Property(f => f.Extension).UseCollation("NOCASE");
        });

        ConfigureTagging(modelBuilder, isSqlServer);
        ConfigureMissingFiles(modelBuilder, isSqlServer);
        ConfigureTrash(modelBuilder);
        ConfigureTasks(modelBuilder);
    }

    /// <summary>Schedules and run history of the background tasks. Enums are stored by name, readable in the table.</summary>
    private static void ConfigureTasks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskSchedule>(entity =>
        {
            entity.ToTable("TaskSchedules");
            entity.Property(s => s.Task).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
            entity.Property(s => s.Kind).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
            entity.Property(s => s.Path).HasMaxLength(MaxPathLength);
        });

        modelBuilder.Entity<TaskRun>(entity =>
        {
            entity.ToTable("TaskRuns");
            entity.Property(r => r.Task).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
            entity.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
            entity.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
            entity.Property(r => r.Path).HasMaxLength(MaxPathLength);
            entity.Property(r => r.Summary).HasMaxLength(1000);
            // The history filtered by task, newest first; the purge of old runs.
            entity.HasIndex(r => new { r.Task, r.Id });
            entity.HasIndex(r => r.Started);
        });
    }

    /// <summary>Files and folders deleted to the trash, with the tags of their files (see <see cref="TrashEntry"/>).</summary>
    private static void ConfigureTrash(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrashEntry>(entity =>
        {
            entity.ToTable("TrashEntries");
            // Not indexed: no length limit (a folder that was never indexed can have a longer path).
            entity.Property(t => t.Path).IsRequired();
            entity.Property(t => t.Name).HasMaxLength(MaxPathLength).IsRequired();
            entity.Property(t => t.DeletedBy).HasMaxLength(200);
            entity.Property(t => t.Location).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
            entity.Property(t => t.Folder).HasMaxLength(32).IsUnicode(false).IsRequired();
            entity.Property(t => t.Kind).HasConversion<string>().HasMaxLength(24).IsUnicode(false);
            // Newest first, and the purge of the oldest.
            entity.HasIndex(t => t.DeletedAt);
        });

        modelBuilder.Entity<TrashTag>(entity =>
        {
            entity.ToTable("TrashTags");
            entity.Property(t => t.Path).IsRequired();
            entity.HasIndex(t => t.TrashEntryId);
            entity.HasIndex(t => t.TagId);
            entity.HasOne(t => t.Entry).WithMany(e => e.Tags).HasForeignKey(t => t.TrashEntryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(t => t.Tag).WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>Tagged files no longer found by a scan, kept for relinking (see <see cref="MissingFile"/>).</summary>
    private static void ConfigureMissingFiles(ModelBuilder modelBuilder, bool isSqlServer)
    {
        modelBuilder.Entity<MissingFile>(entity =>
        {
            entity.ToTable("MissingFiles");
            // Not unique: a file can go missing, and another one later go missing from the same place.
            entity.Property(m => m.FullName).HasMaxLength(MaxPathLength).IsRequired();
            entity.Property(m => m.Name).HasMaxLength(MaxPathLength).IsRequired();
            entity.Property(m => m.Extension).HasMaxLength(MaxPathLength);
            entity.Property(m => m.Sha1).HasMaxLength(40).IsUnicode(false);
            entity.HasIndex(m => m.FullName);
            entity.HasIndex(m => m.MissingSince);
            if (!isSqlServer)
            {
                if (LibraryPaths.IsCaseInsensitiveFileSystem)
                    entity.Property(m => m.FullName).UseCollation("NOCASE");
                entity.Property(m => m.Name).UseCollation("NOCASE");
                entity.Property(m => m.Extension).UseCollation("NOCASE");
            }
        });

        modelBuilder.Entity<MissingFileTag>(entity =>
        {
            entity.ToTable("MissingFileTags");
            entity.HasKey(l => new { l.MissingFileId, l.TagId });
            entity.HasIndex(l => l.TagId);
            entity.HasOne(l => l.MissingFile)
                .WithMany(m => m.TagLinks)
                .HasForeignKey(l => l.MissingFileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(l => l.Tag)
                .WithMany()
                .HasForeignKey(l => l.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>
    /// Tagging tables. Links between tags never cascade (SQL Server rejects multiple cascade paths): <see cref="Tagging.TagService"/>
    /// removes them before deleting a tag.
    /// </summary>
    private static void ConfigureTagging(ModelBuilder modelBuilder, bool isSqlServer)
    {
        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("TagDefinitions");
            entity.Property(t => t.Name).HasMaxLength(MaxTagNameLength).IsRequired();
            entity.Property(t => t.Shorthand).HasMaxLength(MaxTagNameLength);
            entity.Property(t => t.Color).HasMaxLength(32).IsUnicode(false);
            entity.HasIndex(t => t.Name);
            entity.HasOne(t => t.Disambiguation)
                .WithMany()
                .HasForeignKey(t => t.DisambiguationId)
                .OnDelete(DeleteBehavior.NoAction);
            if (!isSqlServer)
            {
                entity.Property(t => t.Name).UseCollation("NOCASE");
                entity.Property(t => t.Shorthand).UseCollation("NOCASE");
            }

            entity.HasData(BuiltInTags.Tags);
        });

        modelBuilder.Entity<TagAlias>(entity =>
        {
            entity.ToTable("TagAliases");
            entity.Property(a => a.Name).HasMaxLength(MaxTagNameLength).IsRequired();
            entity.HasIndex(a => a.Name);
            entity.HasOne(a => a.Tag)
                .WithMany(t => t.Aliases)
                .HasForeignKey(a => a.TagId)
                .OnDelete(DeleteBehavior.Cascade);
            if (!isSqlServer)
                entity.Property(a => a.Name).UseCollation("NOCASE");

            entity.HasData(BuiltInTags.Aliases);
        });

        modelBuilder.Entity<TagParent>(entity =>
        {
            entity.ToTable("TagParents");
            entity.HasKey(p => new { p.ParentId, p.ChildId });
            entity.HasIndex(p => p.ChildId);
            entity.HasOne(p => p.Parent)
                .WithMany(t => t.ChildLinks)
                .HasForeignKey(p => p.ParentId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(p => p.Child)
                .WithMany(t => t.ParentLinks)
                .HasForeignKey(p => p.ChildId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasData(BuiltInTags.Parents);
        });

        modelBuilder.Entity<FileTag>(entity =>
        {
            entity.ToTable("FileTags");
            entity.HasKey(l => new { l.FileId, l.TagId });
            entity.HasIndex(l => l.TagId);
            entity.HasOne(l => l.File)
                .WithMany(f => f.TagLinks)
                .HasForeignKey(l => l.FileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(l => l.Tag)
                .WithMany(t => t.FileLinks)
                .HasForeignKey(l => l.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // A table of its own rather than columns on TagDefinitions: an existing SQLite index can be given missing
        // tables (DatabaseInitializer), not missing columns.
        modelBuilder.Entity<TagActivity>(entity =>
        {
            entity.ToTable("TagActivity");
            entity.HasKey(a => a.TagId);
            entity.HasIndex(a => a.LastSearchedAt);
            entity.HasIndex(a => a.LastViewedAt);
            entity.HasOne(a => a.Tag)
                .WithOne(t => t.Activity)
                .HasForeignKey<TagActivity>(a => a.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureEntry<T>(EntityTypeBuilder<T> entity, bool isSqlServer) where T : IndexEntry
    {
        entity.Property(e => e.FullName).HasMaxLength(MaxPathLength).IsRequired();
        entity.Property(e => e.Name).HasMaxLength(MaxPathLength).IsRequired();
        entity.HasIndex(e => e.FullName).IsUnique();
        entity.HasIndex(e => e.Name);

        if (!isSqlServer)
        {
            // Names are matched case-insensitively, like SQL Server's default collation does. Full paths are the
            // unique key, so they follow the file system: on Linux "a.jpg" and "A.jpg" are two different files.
            if (LibraryPaths.IsCaseInsensitiveFileSystem)
                entity.Property(e => e.FullName).UseCollation("NOCASE");
            entity.Property(e => e.Name).UseCollation("NOCASE");
        }
    }

    /// <summary>
    /// Longest path the index can store, or null when unlimited. SQL Server columns are NVARCHAR(260) (indexed
    /// strings need a length); SQLite does not enforce one.
    /// </summary>
    public int? PathLengthLimit => Database.IsSqlServer() ? MaxPathLength : null;
}
