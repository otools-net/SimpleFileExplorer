using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// Where the unfinished uploads of <see cref="Uploads"/> live: <c>uploads/{id}.part</c> (the bytes received) and its
/// <c>{id}.json</c> (the <see cref="UploadInfo"/>, written through a temporary file) in the work folder, read into
/// memory on first use, and again when the work folder moves (uploads in the old place are then forgotten).
/// </summary>
public sealed class UploadRecords(WorkFolder work, ILogger<Uploads> logger)
{
    public const string FolderName = "uploads";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Lock _loadGate = new();
    private ConcurrentDictionary<string, UploadInfo>? _uploads;
    private string? _loadedFrom;

    /// <summary>The folder holding the uploads' files.</summary>
    public string Folder => work.Get(FolderName);

    /// <summary>Every unfinished upload, by id.</summary>
    public ConcurrentDictionary<string, UploadInfo> All() => Load();

    public UploadInfo? Find(string? id) => id is not null && Load().TryGetValue(id, out var upload) ? upload : null;

    /// <summary>Writes the record and keeps it in memory.</summary>
    public void Save(UploadInfo upload)
    {
        var record = RecordPath(upload.Id);
        var temporary = record + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(upload, Json));
        File.Move(temporary, record, overwrite: true);
        Load()[upload.Id] = upload;
    }

    /// <summary>Forgets the upload and removes its files.</summary>
    public void Remove(UploadInfo upload)
    {
        Load().TryRemove(upload.Id, out _);
        foreach (var file in new[] { PartPath(upload.Id), RecordPath(upload.Id) })
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not remove {File}", Path.GetFileName(file));
            }
        }
    }

    /// <summary>The file holding the bytes received.</summary>
    public string PartPath(string id) => Path.Combine(Folder, $"{SafeId(id)}.part");

    private string RecordPath(string id) => Path.Combine(Folder, $"{SafeId(id)}.json");

    /// <summary>The uploads recorded in the work folder, read once (again when the work folder moves).</summary>
    private ConcurrentDictionary<string, UploadInfo> Load()
    {
        var folder = Folder;
        lock (_loadGate)
        {
            if (_uploads is not null && _loadedFrom == folder)
                return _uploads;
            var uploads = new ConcurrentDictionary<string, UploadInfo>();
            foreach (var record in Directory.EnumerateFiles(folder, "*.json"))
            {
                if (Read(record) is { } upload)
                    uploads[upload.Id] = upload;
            }
            _loadedFrom = folder;
            return _uploads = uploads;
        }
    }

    /// <summary>A record written by <see cref="Save"/>; null when it can't be read or names another upload.</summary>
    private UploadInfo? Read(string record)
    {
        try
        {
            return
                JsonSerializer.Deserialize<UploadInfo>(File.ReadAllText(record), Json) is { } upload
                && upload.Id == Path.GetFileNameWithoutExtension(record)
                ? upload
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read the upload record {File}", Path.GetFileName(record));
            return null;
        }
    }

    /// <summary>Ids are 32 hexadecimal digits: anything else never names a file.</summary>
    private static string SafeId(string id) =>
        id.Length == 32 && id.All(Uri.IsHexDigit) ? id : throw new ArgumentException("Not an upload id.", nameof(id));
}
