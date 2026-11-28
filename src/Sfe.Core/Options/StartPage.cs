using System.Text.Json.Serialization;

namespace Otools.Net.Sfe.Core.Options;

/// <summary>
/// The page the web client opens at its address alone (a bookmark of the server, a new tab): written as a name in the
/// settings file, as <see cref="Theme"/> is.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StartPage>))]
public enum StartPage
{
    /// <summary>The dashboard: home folder, quick links, storage and more (the default since 2.0).</summary>
    Dashboard,

    /// <summary>The account's home folder in the space; the top of the library without one.</summary>
    Home,

    /// <summary>The top of the library (the only start before 2.0).</summary>
    Root,
}
