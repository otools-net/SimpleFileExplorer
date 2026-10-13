using Microsoft.EntityFrameworkCore;

namespace Sfe.Core.Data;

/// <summary>
/// Libraries (tenants), accounts, groups, rights and the settings of libraries and users: one set for the whole
/// server, in the database of <c>ConnectionStrings:Sfe</c> next to the first library's index. A context of its own,
/// with its own migrations (<c>Data/AccessMigrations</c>), so that the other libraries' index databases don't carry it.
/// </summary>
public class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options)
{
    public const string MigrationsHistoryTable = "__AccessMigrationsHistory";
    public const int MaxUserNameLength = 100;
    public const int MaxRightLength = 64;
    public const int MaxSettingKeyLength = 200;

    /// <summary>Found by name before the model can be trusted (<see cref="Upgrades.InstalledVersion"/>).</summary>
    public const string ServerPropertiesTable = "ServerProperties";

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<UserGroup> Groups => Set<UserGroup>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<GroupRight> GroupRights => Set<GroupRight>();
    public DbSet<TenantDisabledRight> TenantDisabledRights => Set<TenantDisabledRight>();
    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();
    public DbSet<ServerProperty> ServerProperties => Set<ServerProperty>();
    public DbSet<QuickLink> QuickLinks => Set<QuickLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var isSqlServer = Database.IsSqlServer();
        if (isSqlServer)
            modelBuilder.HasDefaultSchema(SfeDbContext.Schema);

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("Tenants");
            entity.Property(t => t.Slug).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(t => t.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(t => t.Slug).IsUnique();
            if (!isSqlServer)
                entity.Property(t => t.Slug).UseCollation("NOCASE");
        });

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.ToTable("Users");
            entity.Property(u => u.UserName).HasMaxLength(MaxUserNameLength).IsRequired();
            entity.Property(u => u.DisplayName).HasMaxLength(200);
            entity.Property(u => u.Source).HasMaxLength(32).IsUnicode(false).IsRequired();
            entity.Property(u => u.ExternalId).HasMaxLength(400);
            entity.Property(u => u.Kind).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
            entity.Property(u => u.SecurityStamp).HasMaxLength(64).IsUnicode(false).IsRequired();
            // One account per name, whatever the case (SQL Server's default collation ignores it; SQLite needs NOCASE).
            entity.HasIndex(u => u.UserName).IsUnique();
            // A directory account is found again by the directory's own id, whatever its name has become.
            entity.HasIndex(u => new { u.Source, u.ExternalId }).IsUnique();
            if (!isSqlServer)
                entity.Property(u => u.UserName).UseCollation("NOCASE");
        });

        modelBuilder.Entity<UserGroup>(entity =>
        {
            entity.ToTable("Groups");
            entity.Property(g => g.Name).HasMaxLength(100).IsRequired();
            entity.Property(g => g.BuiltIn).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
            entity.Property(g => g.ExternalName).HasMaxLength(400);
            entity.HasIndex(g => new { g.TenantId, g.Name }).IsUnique();
            entity.HasOne(g => g.Tenant)
                .WithMany(t => t.Groups)
                .HasForeignKey(g => g.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
            if (!isSqlServer)
                entity.Property(g => g.Name).UseCollation("NOCASE");
        });

        modelBuilder.Entity<GroupMember>(entity =>
        {
            entity.ToTable("GroupMembers");
            entity.HasKey(m => new { m.GroupId, m.UserId });
            entity.HasIndex(m => m.UserId);
            entity.HasOne(m => m.Group)
                .WithMany(g => g.Members)
                .HasForeignKey(m => m.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(m => m.User)
                .WithMany(u => u.Groups)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GroupRight>(entity =>
        {
            entity.ToTable("GroupRights");
            entity.HasKey(r => new { r.GroupId, r.Right });
            entity.Property(r => r.Right).HasMaxLength(MaxRightLength).IsUnicode(false);
            entity.HasOne(r => r.Group)
                .WithMany(g => g.Rights)
                .HasForeignKey(r => r.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenantDisabledRight>(entity =>
        {
            entity.ToTable("TenantDisabledRights");
            entity.HasKey(r => new { r.TenantId, r.Right });
            entity.Property(r => r.Right).HasMaxLength(MaxRightLength).IsUnicode(false);
            entity.HasOne(r => r.Tenant)
                .WithMany(t => t.DisabledRights)
                .HasForeignKey(r => r.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenantSetting>(entity =>
        {
            entity.ToTable("TenantSettings");
            entity.HasKey(s => new { s.TenantId, s.Key });
            entity.Property(s => s.Key).HasMaxLength(MaxSettingKeyLength).IsUnicode(false);
            entity.Property(s => s.Value).IsRequired();
            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(s => s.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserSetting>(entity =>
        {
            entity.ToTable("UserSettings");
            entity.HasKey(s => new { s.UserId, s.Key });
            entity.Property(s => s.Key).HasMaxLength(MaxSettingKeyLength).IsUnicode(false);
            entity.Property(s => s.Value).IsRequired();
            entity.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ServerProperty>(entity =>
        {
            entity.ToTable(ServerPropertiesTable);
            entity.HasKey(p => p.Key);
            entity.Property(p => p.Key).HasMaxLength(64).IsUnicode(false);
            entity.Property(p => p.Value).HasMaxLength(400).IsRequired();
        });

        modelBuilder.Entity<QuickLink>(entity =>
        {
            entity.ToTable("QuickLinks");
            entity.Property(l => l.Path).HasMaxLength(SfeDbContext.MaxPathLength).IsRequired();
            // Not unique: paths compare as the host file system does (case-sensitive on Linux), which SQL Server's
            // collation doesn't; QuickLinkService keeps one row per path.
            entity.HasIndex(l => new { l.UserId, l.TenantId });
            entity.HasIndex(l => l.TenantId);
            entity.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(l => l.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
