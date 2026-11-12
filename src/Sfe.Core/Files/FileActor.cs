namespace Sfe.Core.Files;

/// <summary>Who changes the library's files, for the log and the folder rules' history.</summary>
public sealed record FileActor(int? AccountId, string Name);
