namespace Otools.Net.Sfe.Core.Files;

/// <summary>A folder of a job merged into the folder of its name in the target (<see cref="FileJobs"/>).</summary>
/// <param name="Source">The folder merged, full path.</param>
/// <param name="SourcePath">The folder merged, in the library.</param>
/// <param name="Into">The folder it goes into, full path.</param>
/// <param name="IntoPath">The folder it goes into, in the library.</param>
internal sealed record MergedFolder(string Source, string SourcePath, string Into, string IntoPath);
