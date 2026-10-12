namespace Sfe.Core.Access;

/// <summary>
/// Counts the changes to accounts, groups, rights and libraries. The caches that read them (rights, sessions, setup
/// state) keep an entry only while the count is the one they read it at, so a change applies to the next request.
/// </summary>
public sealed class AccessChanges
{
    private long _version;

    public long Version => Interlocked.Read(ref _version);

    /// <summary>Raised after the settings of a library changed, with its id, so that its options are read again.</summary>
    public event Action<int>? TenantSettingsChanged;

    public void Changed() => Interlocked.Increment(ref _version);

    public void SettingsChanged(int tenantId)
    {
        Changed();
        TenantSettingsChanged?.Invoke(tenantId);
    }
}
