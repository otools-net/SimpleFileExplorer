namespace Otools.Net.Sfe.Api.Maintenance;

public enum MigrationStatus
{
    /// <summary>The install belongs to this version: the app works.</summary>
    None,

    /// <summary>The install belongs to an earlier version: nothing works until its update is confirmed.</summary>
    Required,

    Running,

    /// <summary>The update stopped on an error: it can be started again.</summary>
    Failed,

    /// <summary>The install belongs to a later version: this one must not touch it.</summary>
    Newer,

    /// <summary>
    /// The database could not be read at all (e.g. SQL Server not started yet): checked again from time to time.
    /// </summary>
    Unavailable,
}
