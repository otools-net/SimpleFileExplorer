using System.Text.Json;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// Where the unfinished uploads into connections (<see cref="RemoteUploads"/>) are kept across restarts: one
/// <c>{id}.json</c> per upload (written through a temporary file, readable by the server's account only) in a folder
/// beside the setup code, the provider's session protected by the server's keys. A record that can't be read, or
/// whose session doesn't unprotect (other keys), is removed: its upload starts again.
/// </summary>
public sealed class RemoteUploadRecords(string folder, ICredentialProtector protector, ILogger<RemoteUploads> logger)
{
    public const string FolderName = "connection-uploads";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Folder { get; } = folder;

    /// <summary>The uploads recorded; those that can't be used any more are removed.</summary>
    public IReadOnlyList<RemoteUpload> ReadAll()
    {
        if (!Directory.Exists(Folder))
            return [];
        List<RemoteUpload> uploads = [];
        foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
        {
            if (Read(file) is { } upload)
                uploads.Add(upload);
            else
                Delete(file);
        }
        return uploads;
    }

    /// <summary>Writes the upload's record; a failure is logged (the upload goes on, held in memory only).</summary>
    public void Save(RemoteUpload upload)
    {
        try
        {
            SecretFiles.CreatePrivateDirectory(Folder);
            var record = RecordPath(upload.Id);
            var temporary = record + ".tmp";
            SecretFiles.WriteAllText(temporary, JsonSerializer.Serialize(ToRecord(upload), Json));
            File.Move(temporary, record, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not keep the record of the upload {Id} into a connection", upload.Id);
        }
    }

    /// <summary>Removes the upload's record.</summary>
    public void Remove(RemoteUpload upload) => Delete(RecordPath(upload.Id));

    private RemoteUpload? Read(string file)
    {
        RemoteUploadRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<RemoteUploadRecord>(File.ReadAllText(file), Json);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read the upload record {File}", Path.GetFileName(file));
            return null;
        }
        if (record is null || !IsId(record.Id) || record.Id != Path.GetFileNameWithoutExtension(file))
            return null;
        return protector.Unprotect(record.ProtectedSession) is { Length: > 0 } session
            ? FromRecord(record, session)
            : null;
    }

    private RemoteUploadRecord ToRecord(RemoteUpload upload) =>
        new()
        {
            Id = upload.Id,
            ConnectionId = upload.ConnectionId,
            TenantId = upload.TenantId,
            OwnerId = upload.OwnerId,
            OwnerName = upload.OwnerName,
            Target = upload.Target,
            Name = upload.Name,
            Size = upload.Size,
            Modified = upload.Modified,
            ProtectedSession = protector.Protect(upload.Session),
            Started = upload.Started,
            Received = upload.Received,
            LastActivity = upload.LastActivity,
        };

    private static RemoteUpload FromRecord(RemoteUploadRecord record, string session) =>
        new()
        {
            Id = record.Id,
            ConnectionId = record.ConnectionId,
            TenantId = record.TenantId,
            OwnerId = record.OwnerId,
            OwnerName = record.OwnerName,
            Target = record.Target,
            Name = record.Name,
            Size = record.Size,
            Modified = record.Modified,
            Session = session,
            Started = record.Started,
            Received = record.Received,
            LastActivity = record.LastActivity,
        };

    private void Delete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not remove the upload record {File}", Path.GetFileName(file));
        }
    }

    private string RecordPath(string id) =>
        IsId(id) ? Path.Combine(Folder, $"{id}.json") : throw new ArgumentException("Not an upload id.", nameof(id));

    /// <summary>Ids are 32 lowercase hexadecimal digits: anything else never names a file.</summary>
    private static bool IsId(string? id) => id is { Length: 32 } && id.All(char.IsAsciiHexDigitLower);
}
