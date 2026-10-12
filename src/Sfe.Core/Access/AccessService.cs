using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Options;

namespace Sfe.Core.Access;

/// <summary>
/// Libraries (tenants), accounts and groups. Checks what keeps the server usable (a name per account, a last
/// administrator, the built-in groups) but not who asks: the Api decides that from the caller's rights.
/// </summary>
public sealed partial class AccessService(
    IDbContextFactory<AccessDbContext> dbFactory, AccessChanges changes, IOptionsMonitor<SecurityOptions> security)
{
    public const string DefaultTenantSlug = "library";
    public const int MaxNameLength = 200;

    private readonly ConcurrentDictionary<int, (long Version, SessionUser? User)> _sessions = new();
    private (long Version, IReadOnlyList<TenantInfo> Tenants)? _tenants;

    // ---- Libraries ----

    /// <summary>Every library, read on each request to find the request's one: cached until the next change.</summary>
    public async Task<IReadOnlyList<TenantInfo>> GetCachedTenantsAsync(CancellationToken cancellationToken = default)
    {
        var version = changes.Version;
        if (_tenants is { } cached && cached.Version == version)
            return cached.Tenants;

        var tenants = await GetTenantsAsync(cancellationToken);
        _tenants = (version, tenants);
        return tenants;
    }

    public async Task<IReadOnlyList<TenantInfo>> GetTenantsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tenants = await db.Tenants.AsNoTracking().Include(t => t.DisabledRights).OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name).ToListAsync(cancellationToken);
        return tenants.Select(ToInfo).ToList();
    }

    public async Task<TenantInfo?> GetTenantAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Tenants.AsNoTracking().Include(t => t.DisabledRights).FirstOrDefaultAsync(t => t.Id == id, cancellationToken) is { } tenant
            ? ToInfo(tenant)
            : null;
    }

    public async Task<TenantInfo?> FindTenantAsync(string slug, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var lowered = slug.Trim().ToLowerInvariant();
        return await db.Tenants.AsNoTracking().Include(t => t.DisabledRights).FirstOrDefaultAsync(t => t.Slug == lowered, cancellationToken) is { } tenant
            ? ToInfo(tenant)
            : null;
    }

    /// <summary>
    /// The library of the configuration, created on the first start of a version with accounts: the existing index,
    /// tags and settings become its own, unchanged.
    /// </summary>
    public async Task<TenantInfo> EnsureDefaultTenantAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Tenants.AsNoTracking().Include(t => t.DisabledRights).FirstOrDefaultAsync(t => t.IsDefault, cancellationToken);
        if (existing is not null)
            return ToInfo(existing);

        var tenant = NewTenant(name, DefaultTenantSlug);
        tenant.IsDefault = true;
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
        return ToInfo(tenant);
    }

    public async Task<AccessResult<TenantInfo>> CreateTenantAsync(TenantInput input, string root, CancellationToken cancellationToken = default)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            return AccessResult<TenantInfo>.Invalid("name", $"A space needs a name of at most {MaxNameLength} characters.");

        var slug = string.IsNullOrWhiteSpace(input.Slug) ? Slugify(name) : input.Slug.Trim().ToLowerInvariant();
        if (!SlugRegex().IsMatch(slug))
            return AccessResult<TenantInfo>.Invalid("slug", "The short name can only have lowercase letters, digits and '-' (at most 64), starting with a letter or digit.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(input.Slug))
            slug = await UniqueSlugAsync(db, slug, cancellationToken);
        else if (await db.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken))
            return AccessResult<TenantInfo>.Invalid("slug", $"Another space has the short name “{slug}”.");

        var tenant = NewTenant(name, slug);
        tenant.Root = root;
        tenant.GuestAccess = input.GuestAccess;
        tenant.Disabled = input.Disabled;
        if (DisabledRightsError(input.DisabledRights) is { } error)
            return AccessResult<TenantInfo>.Invalid("disabledRights", error);
        tenant.DisabledRights = (input.DisabledRights ?? []).Distinct().Select(r => new TenantDisabledRight { Right = r }).ToList();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
        return AccessResult<TenantInfo>.Ok(ToInfo(tenant));
    }

    /// <summary>Changes the name, the guest access, the disabled rights and the status of a library (not its slug or root).</summary>
    public async Task<AccessResult<TenantInfo>> UpdateTenantAsync(int id, TenantInput input, CancellationToken cancellationToken = default)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            return AccessResult<TenantInfo>.Invalid("name", $"A space needs a name of at most {MaxNameLength} characters.");
        if (DisabledRightsError(input.DisabledRights) is { } error)
            return AccessResult<TenantInfo>.Invalid("disabledRights", error);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tenant = await db.Tenants.Include(t => t.DisabledRights).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
            return AccessResult<TenantInfo>.Missing();
        if (input.Disabled && tenant.IsDefault)
            return AccessResult<TenantInfo>.Conflict("The first space cannot be disabled.");

        tenant.Name = name;
        tenant.GuestAccess = input.GuestAccess;
        tenant.Disabled = input.Disabled;
        if (input.DisabledRights is { } rights)
        {
            tenant.DisabledRights.Clear();
            tenant.DisabledRights.AddRange(rights.Distinct().Select(r => new TenantDisabledRight { TenantId = id, Right = r }));
        }

        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
        return AccessResult<TenantInfo>.Ok(ToInfo(tenant));
    }

    /// <summary>Lets visitors without an account in, or not (the part of a library its own administrators change).</summary>
    public async Task<TenantInfo?> SetGuestAccessAsync(int id, bool guestAccess, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tenant = await db.Tenants.Include(t => t.DisabledRights).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
            return null;

        tenant.GuestAccess = guestAccess;
        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
        return ToInfo(tenant);
    }

    /// <summary>Forgets a library's groups and settings (its index database and caches are left on the disk).</summary>
    public async Task<AccessResult<TenantInfo>> DeleteTenantAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tenant = await db.Tenants.Include(t => t.DisabledRights).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
            return AccessResult<TenantInfo>.Missing();
        if (tenant.IsDefault)
            return AccessResult<TenantInfo>.Conflict("The first space cannot be deleted.");

        db.Tenants.Remove(tenant);
        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
        return AccessResult<TenantInfo>.Ok(ToInfo(tenant));
    }

    // ---- Accounts ----

    /// <param name="tenantId">Only the accounts with a group in this library; null = every account.</param>
    public async Task<IReadOnlyList<UserInfo>> GetUsersAsync(int? tenantId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Users.AsNoTracking().Include(u => u.Groups).ThenInclude(m => m.Group).AsQueryable();
        if (tenantId is { } tenant)
            query = query.Where(u => u.Groups.Any(m => m.Group!.TenantId == tenant));
        var users = await query.OrderBy(u => u.UserName).ToListAsync(cancellationToken);
        return users.Select(ToInfo).ToList();
    }

    public async Task<UserInfo?> GetUserAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AsNoTracking().Include(u => u.Groups).ThenInclude(m => m.Group).FirstOrDefaultAsync(u => u.Id == id, cancellationToken) is { } user
            ? ToInfo(user)
            : null;
    }

    /// <summary>
    /// The account a session or a link was made for, as it is now (cached until the next change of any account):
    /// asked on every request, to end the sessions of disabled accounts and changed passwords at once.
    /// </summary>
    public async Task<SessionUser?> GetSessionUserAsync(int id, CancellationToken cancellationToken = default)
    {
        var version = changes.Version;
        if (_sessions.TryGetValue(id, out var cached) && cached.Version == version)
            return cached.User;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == id)
            .Select(u => new SessionUser(u.Id, u.UserName, u.DisplayName, u.Source, u.Kind, u.IsServerAdmin, u.Disabled, u.SecurityStamp))
            .FirstOrDefaultAsync(cancellationToken);
        _sessions[id] = (version, user);
        return user;
    }

    /// <summary>Notes when the account last signed in (shown on the accounts page).</summary>
    public async Task RecordSignInAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.LastSignIn, DateTime.UtcNow), cancellationToken);
    }

    /// <summary>Whether an enabled server administrator exists (the setup is done).</summary>
    public async Task<bool> HasServerAdminAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AnyAsync(u => u.IsServerAdmin && !u.Disabled, cancellationToken);
    }

    /// <param name="tenantScope">
    /// The library whose groups <see cref="UserInput.GroupIds"/> may name (a library's own administrator); null = any.
    /// </param>
    public async Task<AccessResult<UserInfo>> CreateUserAsync(UserInput input, int? tenantScope, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var userName = input.UserName?.Trim() ?? string.Empty;
        if (UserNameError(userName) is { } nameError)
            return AccessResult<UserInfo>.Invalid("userName", nameError);
        if (await db.Users.AnyAsync(u => u.UserName == userName, cancellationToken))
            return AccessResult<UserInfo>.Invalid("userName", $"There is already an account named “{userName}”.");
        if (PasswordError(input.Password) is { } passwordError)
            return AccessResult<UserInfo>.Invalid("password", passwordError);
        if (KindError(input) is { } kindError)
            return AccessResult<UserInfo>.Invalid("kind", kindError);
        if (DisplayNameError(input.DisplayName) is { } displayError)
            return AccessResult<UserInfo>.Invalid("displayName", displayError);

        var user = new UserAccount
        {
            UserName = userName,
            DisplayName = Clean(input.DisplayName),
            PasswordHash = PasswordHashing.Hash(input.Password!),
            Kind = input.Kind,
            IsServerAdmin = input.IsServerAdmin,
            Disabled = input.Disabled,
            SecurityStamp = PasswordHashing.NewStamp(),
            Created = DateTime.UtcNow,
        };
        db.Users.Add(user);
        if (await ReplaceGroupsAsync(db, user, input.GroupIds ?? [], tenantScope, cancellationToken) is { } groupError)
            return AccessResult<UserInfo>.Invalid("groupIds", groupError);

        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
        return AccessResult<UserInfo>.Ok((await GetUserAsync(user.Id, cancellationToken))!);
    }

    /// <param name="tenantScope">
    /// A library's own administrator: only the account's groups in that library change, and the server administrator
    /// status stays as it is.
    /// </param>
    public async Task<AccessResult<UserInfo>> UpdateUserAsync(int id, UserInput input, int? tenantScope, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.Include(u => u.Groups).ThenInclude(m => m.Group).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
            return AccessResult<UserInfo>.Missing();

        var userName = input.UserName?.Trim() ?? string.Empty;
        if (UserNameError(userName) is { } nameError)
            return AccessResult<UserInfo>.Invalid("userName", nameError);
        if (!string.Equals(userName, user.UserName, StringComparison.Ordinal)
            && await db.Users.AnyAsync(u => u.Id != id && u.UserName == userName, cancellationToken))
            return AccessResult<UserInfo>.Invalid("userName", $"There is already an account named “{userName}”.");
        if (input.Password is not null && !IsLocal(user))
            return AccessResult<UserInfo>.Invalid("password", "The password of this account is kept by its directory.");
        if (input.Password is not null && PasswordError(input.Password) is { } passwordError)
            return AccessResult<UserInfo>.Invalid("password", passwordError);
        var isServerAdmin = tenantScope is null ? input.IsServerAdmin : user.IsServerAdmin;
        if (KindError(input with { IsServerAdmin = isServerAdmin }) is { } kindError)
            return AccessResult<UserInfo>.Invalid("kind", kindError);
        if (DisplayNameError(input.DisplayName) is { } displayError)
            return AccessResult<UserInfo>.Invalid("displayName", displayError);

        var losesAdmin = user.IsServerAdmin && !user.Disabled && (!isServerAdmin || input.Disabled);
        if (losesAdmin && !await db.Users.AnyAsync(u => u.Id != id && u.IsServerAdmin && !u.Disabled, cancellationToken))
            return AccessResult<UserInfo>.Conflict("This is the last server administrator: make another one first.");

        // A new status or password ends the account's sessions and the links it gave out.
        var changesAccess = input.Password is not null || input.Disabled != user.Disabled || isServerAdmin != user.IsServerAdmin || input.Kind != user.Kind;
        if (!IsLocal(user))
            userName = user.UserName;
        user.UserName = userName;
        user.DisplayName = Clean(input.DisplayName);
        user.Kind = input.Kind;
        user.IsServerAdmin = isServerAdmin;
        user.Disabled = input.Disabled;
        if (input.Password is not null)
            user.PasswordHash = PasswordHashing.Hash(input.Password);
        if (input.Unlock)
        {
            user.FailedSignIns = 0;
            user.LockedUntil = null;
        }

        if (changesAccess)
            user.SecurityStamp = PasswordHashing.NewStamp();
        if (input.GroupIds is { } groupIds && await ReplaceGroupsAsync(db, user, groupIds, tenantScope, cancellationToken) is { } groupError)
            return AccessResult<UserInfo>.Invalid("groupIds", groupError);

        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
        return AccessResult<UserInfo>.Ok((await GetUserAsync(id, cancellationToken))!);
    }

    public async Task<AccessResult<UserInfo>> DeleteUserAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.Include(u => u.Groups).ThenInclude(m => m.Group).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
            return AccessResult<UserInfo>.Missing();
        if (user.IsServerAdmin && !user.Disabled && !await db.Users.AnyAsync(u => u.Id != id && u.IsServerAdmin && !u.Disabled, cancellationToken))
            return AccessResult<UserInfo>.Conflict("This is the last server administrator: make another one first.");

        var info = ToInfo(user);
        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
        return AccessResult<UserInfo>.Ok(info);
    }

    /// <summary>Changes the password of a local account after checking its current one.</summary>
    public async Task<AccessResult<UserInfo>> ChangeOwnPasswordAsync(int id, string? current, string? password, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
            return AccessResult<UserInfo>.Missing();
        if (!IsLocal(user))
            return AccessResult<UserInfo>.Invalid("password", "The password of this account is kept by its directory.");
        if (!PasswordHashing.Verify(current ?? string.Empty, user.PasswordHash, out _))
            return AccessResult<UserInfo>.Invalid("current", "This is not the current password.");
        if (PasswordError(password) is { } error)
            return AccessResult<UserInfo>.Invalid("password", error);

        user.PasswordHash = PasswordHashing.Hash(password!);
        user.SecurityStamp = PasswordHashing.NewStamp();
        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
        return AccessResult<UserInfo>.Ok((await GetUserAsync(id, cancellationToken))!);
    }

    // ---- Groups ----

    /// <param name="tenantId">The space's groups; null = those of every space (the account editor of server administrators).</param>
    public async Task<IReadOnlyList<GroupInfo>> GetGroupsAsync(int? tenantId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Groups.AsNoTracking().Include(g => g.Rights).Include(g => g.Members).AsQueryable();
        if (tenantId is { } tenant)
            query = query.Where(g => g.TenantId == tenant);
        var groups = await query.ToListAsync(cancellationToken);
        return groups
            .OrderBy(g => g.TenantId)
            .ThenBy(g => g.BuiltIn is null).ThenBy(g => g.BuiltIn).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ToInfo)
            .ToList();
    }

    public async Task<AccessResult<GroupInfo>> CreateGroupAsync(int tenantId, GroupInput input, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, cancellationToken))
            return AccessResult<GroupInfo>.Missing();

        var group = new UserGroup { TenantId = tenantId };
        if (await ApplyGroupAsync(db, group, input, cancellationToken) is { } error)
            return error.As<GroupInfo>();

        db.Groups.Add(group);
        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
        return AccessResult<GroupInfo>.Ok(ToInfo(group));
    }

    public async Task<AccessResult<GroupInfo>> UpdateGroupAsync(int tenantId, int id, GroupInput input, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.Groups.Include(g => g.Rights).Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == id && g.TenantId == tenantId, cancellationToken);
        if (group is null)
            return AccessResult<GroupInfo>.Missing();
        if (await ApplyGroupAsync(db, group, input, cancellationToken) is { } error)
            return error.As<GroupInfo>();

        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
        return AccessResult<GroupInfo>.Ok(ToInfo(group));
    }

    public async Task<AccessResult<GroupInfo>> DeleteGroupAsync(int tenantId, int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.Groups.Include(g => g.Rights).Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == id && g.TenantId == tenantId, cancellationToken);
        if (group is null)
            return AccessResult<GroupInfo>.Missing();
        if (group.BuiltIn is not null)
            return AccessResult<GroupInfo>.Conflict("Built-in groups cannot be deleted.");

        db.Groups.Remove(group);
        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
        return AccessResult<GroupInfo>.Ok(ToInfo(group));
    }

    /// <summary>The built-in group of a library (e.g. where the setup puts the first administrator).</summary>
    public async Task<GroupInfo?> GetBuiltInGroupAsync(int tenantId, BuiltInGroup builtIn, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Groups.AsNoTracking().Include(g => g.Rights).Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.BuiltIn == builtIn, cancellationToken) is { } group
            ? ToInfo(group)
            : null;
    }

    /// <summary>Adds an account to a group (no-op when it is in it already).</summary>
    public async Task AddToGroupAsync(int userId, int groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == userId, cancellationToken))
            return;
        db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = userId });
        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
    }

    internal string? PasswordError(string? password)
    {
        var minimum = Math.Max(1, security.CurrentValue.MinimumPasswordLength);
        if (string.IsNullOrEmpty(password) || password.Length < minimum)
            return $"The password needs at least {minimum} characters.";
        return password.Length > 1000 ? "The password can have at most 1000 characters." : null;
    }

    internal static string? UserNameError(string userName)
    {
        if (userName.Length == 0)
            return "An account needs a name.";
        if (userName.Length > AccessDbContext.MaxUserNameLength)
            return $"Account names can have at most {AccessDbContext.MaxUserNameLength} characters.";
        return userName.Any(char.IsControl) ? "Account names cannot contain control characters." : null;
    }

    private static string? DisplayNameError(string? displayName) =>
        displayName is { Length: > MaxNameLength } ? $"The display name can have at most {MaxNameLength} characters." : null;

    private static string? KindError(UserInput input) =>
        input.Kind == UserKind.Guest && input.IsServerAdmin ? "A guest account cannot be a server administrator." : null;

    private static string? DisabledRightsError(IReadOnlyList<string>? rights) =>
        rights?.FirstOrDefault(r => !RightCatalog.IsTenantRight(r)) is { } unknown ? $"“{unknown}” is not a right of a space." : null;

    private static bool IsLocal(UserAccount user) => user.Source == UserAccount.LocalSource;

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Replaces the account's groups among those <paramref name="tenantScope"/> covers; an error for a group outside it.</summary>
    private static async Task<string?> ReplaceGroupsAsync(AccessDbContext db, UserAccount user, IReadOnlyList<int> groupIds, int? tenantScope, CancellationToken cancellationToken)
    {
        var wanted = groupIds.Distinct().ToList();
        var groups = await db.Groups.Where(g => wanted.Contains(g.Id)).ToListAsync(cancellationToken);
        if (groups.Count != wanted.Count)
            return "A group no longer exists: reload the page.";
        if (tenantScope is { } scope && groups.Any(g => g.TenantId != scope))
            return "Groups of other spaces can only be changed by a server administrator.";

        user.Groups.RemoveAll(m => !wanted.Contains(m.GroupId) && (tenantScope is null || m.Group?.TenantId == tenantScope));
        foreach (var group in groups.Where(g => user.Groups.All(m => m.GroupId != g.Id)))
            user.Groups.Add(new GroupMember { GroupId = group.Id, Group = group, User = user });
        return null;
    }

    private static async Task<AccessResult<UserGroup>?> ApplyGroupAsync(AccessDbContext db, UserGroup group, GroupInput input, CancellationToken cancellationToken)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100)
            return AccessResult<UserGroup>.Invalid("name", "A group needs a name of at most 100 characters.");
        if (await db.Groups.AnyAsync(g => g.TenantId == group.TenantId && g.Id != group.Id && g.Name == name, cancellationToken))
            return AccessResult<UserGroup>.Invalid("name", $"There is already a group named “{name}”.");
        if (input.ExternalName is { Length: > 400 })
            return AccessResult<UserGroup>.Invalid("externalName", "The directory group can have at most 400 characters.");

        if (input.Rights is { } rights)
        {
            if (rights.FirstOrDefault(r => !RightCatalog.IsTenantRight(r)) is { } unknown)
                return AccessResult<UserGroup>.Invalid("rights", $"“{unknown}” is not a right of a space.");
            if (group.BuiltIn == BuiltInGroup.Guests && rights.FirstOrDefault(r => RightCatalog.Find(r) is { GuestAllowed: false }) is { } forbidden)
                return AccessResult<UserGroup>.Invalid("rights", $"Guests can never have the right “{RightCatalog.Find(forbidden)!.Title}”.");
            group.Rights.RemoveAll(r => !rights.Contains(r.Right));
            foreach (var right in rights.Distinct().Where(r => group.Rights.All(existing => existing.Right != r)))
                group.Rights.Add(new GroupRight { Right = right });
        }

        if (input.MemberIds is { } memberIds)
        {
            var wanted = memberIds.Distinct().ToList();
            if (await db.Users.CountAsync(u => wanted.Contains(u.Id), cancellationToken) != wanted.Count)
                return AccessResult<UserGroup>.Invalid("memberIds", "An account no longer exists: reload the page.");
            group.Members.RemoveAll(m => !wanted.Contains(m.UserId));
            foreach (var userId in wanted.Where(u => group.Members.All(m => m.UserId != u)))
                group.Members.Add(new GroupMember { UserId = userId });
        }

        // Built-in groups keep their name: the pages and the guest rules find them by it.
        if (group.BuiltIn is null)
            group.Name = name;
        group.ExternalName = Clean(input.ExternalName);
        return null;
    }

    /// <summary>A library with its three built-in groups, granted the catalog's default rights.</summary>
    private static Tenant NewTenant(string name, string slug) => new()
    {
        Name = name,
        Slug = slug,
        Created = DateTime.UtcNow,
        Groups = Enum.GetValues<BuiltInGroup>().Select(builtIn => new UserGroup
        {
            Name = builtIn.ToString(),
            BuiltIn = builtIn,
            Rights = RightCatalog.TenantRights
                .Where(r => r.DefaultGroups.Contains(builtIn))
                .Select(r => new GroupRight { Right = r.Id })
                .ToList(),
        }).ToList(),
    };

    private static async Task<string> UniqueSlugAsync(AccessDbContext db, string slug, CancellationToken cancellationToken)
    {
        var candidate = slug;
        for (var n = 2; await db.Tenants.AnyAsync(t => t.Slug == candidate, cancellationToken); n++)
            candidate = $"{slug[..Math.Min(slug.Length, 60)]}-{n}";
        return candidate;
    }

    /// <summary>"Family Photos (2020)" → "family-photos-2020"; accents dropped, other letters left out.</summary>
    internal static string Slugify(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-' && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        slug = slug.Length > 64 ? slug[..64].TrimEnd('-') : slug;
        return slug.Length == 0 ? "library" : slug;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex SlugRegex();

    private static TenantInfo ToInfo(Tenant t) =>
        new(t.Id, t.Slug, t.Name, t.GuestAccess, t.IsDefault, t.Disabled, t.Root, t.DisabledRights.Select(r => r.Right).Order(StringComparer.Ordinal).ToList(), t.ConnectionString);

    private UserInfo ToInfo(UserAccount u) =>
        new(u.Id, u.UserName, u.DisplayName, u.Source, u.Kind, u.IsServerAdmin, u.Disabled, u.LockedUntil > DateTime.UtcNow, u.Created, u.LastSignIn,
            u.Groups.Where(m => m.Group is not null).Select(m => new MembershipInfo(m.Group!.TenantId, m.GroupId, m.Group.Name)).ToList());

    private static GroupInfo ToInfo(UserGroup g) =>
        new(g.Id, g.TenantId, g.Name, g.BuiltIn, g.ExternalName, g.Rights.Select(r => r.Right).Order(StringComparer.Ordinal).ToList(),
            g.Members.Select(m => m.UserId).Order().ToList());
}
