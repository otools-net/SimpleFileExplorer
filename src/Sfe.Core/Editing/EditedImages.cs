using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Files;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Core.Editing;

/// <summary>The encodings a browser's canvas writes, which are the only ones the editor saves.</summary>
public enum EditedImageFormat
{
    Jpeg,
    Png,
    WebP,
}

public enum EditedImageOutcome
{
    Saved,

    /// <summary>The original is not a picture of the library (missing, outside the root, not an image type).</summary>
    SourceNotFound,

    /// <summary>What was sent is not a JPEG, PNG or WebP picture whose size can be read.</summary>
    InvalidImage,

    /// <summary>What was sent is larger than <see cref="EditedImages.MaxBytes"/>.</summary>
    TooLarge,

    /// <summary><see cref="EditorOptions.OutputFolder"/> is not a folder of the library, or the file cannot be written there.</summary>
    CannotWrite,

    /// <summary>Saved over the original: the edited picture is not in the original's format (a GIF made a PNG...).</summary>
    OtherFormat,

    /// <summary>Saved over the original: <see cref="FileContent.OverwriteAsync"/> did not (see <see cref="EditedImageResult.Written"/>).</summary>
    NotWritten,
}

/// <param name="Path">The new file, relative to the library root.</param>
public sealed record EditedImage(string Path, int Width, int Height, long Size);

/// <param name="Written">Saved over the original: what <see cref="FileContent.OverwriteAsync"/> answered.</param>
public sealed record EditedImageResult(EditedImageOutcome Outcome, EditedImage? Image = null, string? Message = null, OverwriteResult? Written = null);

/// <summary>
/// Saves the pictures made by the web client's editor. The browser applies the edits (crop...) itself and sends the
/// encoded result: the server needs no image library, and a new kind of edit needs no change here. The result is
/// always a new file, in <see cref="EditorOptions.OutputFolder"/> under the original's own folders (or next to the
/// original when the setting is empty), never written over an existing file; it is indexed at once, so that searches
/// find it before the next scan. Or it is saved over the original (<see cref="SaveOverAsync"/>), through
/// <see cref="FileContent"/> as text files are: same version check, previous version in the trash.
/// </summary>
public sealed class EditedImages(
    LibraryPaths paths, MediaClassifier classifier, DirectoryIndexer indexer, FileContent files, IOptionsMonitor<SfeOptions> options, ILogger<EditedImages> logger)
{
    /// <summary>200 MB: a lossless PNG of a 50-megapixel photo.</summary>
    public const long MaxBytes = 200L * 1024 * 1024;

    /// <summary>Lowest quality setting: below it JPEG pictures are mostly blocks.</summary>
    public const int MinQuality = 10;

    /// <summary>Added to a name saved next to its original.</summary>
    public const string EditedSuffix = " (edited)";

    /// <summary>Names tried before giving up (<c>beach (2).jpg</c>, <c>beach (3).jpg</c>...).</summary>
    private const int MaxNameAttempts = 1000;

    /// <summary>Saves <paramref name="content"/> as the edited version of the picture <paramref name="sourcePath"/>.</summary>
    /// <param name="sourcePath">The original, relative to the library root.</param>
    public async Task<EditedImageResult> SaveAsync(string? sourcePath, Stream content, CancellationToken cancellationToken = default)
    {
        if (Source(sourcePath) is not { } found)
            return new(EditedImageOutcome.SourceNotFound, Message: "The original picture was not found.");
        var (source, sourceFull) = found;

        // Read whole before anything is written: the format decides the name, and a broken upload leaves no file.
        using var buffer = new MemoryStream();
        if (await ReadAsync(content, buffer, cancellationToken) is { } problem)
            return problem;
        var (format, size) = (Detect(buffer)!.Value, ReadSize(buffer)!.Value);

        var folder = TargetFolder(source);
        if (folder is null || !paths.TryResolve(folder, out var folderFull))
            return new(EditedImageOutcome.CannotWrite, Message: "The folder for edited pictures is not a folder of the library: check the editor's settings.");

        string target;
        try
        {
            Directory.CreateDirectory(folderFull);
            target = await WriteNewAsync(folderFull, BaseName(source, folder), Extension(sourceFull, format), buffer, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Cannot save the edited version of {Path} in {Folder}", source, folder);
            return new(EditedImageOutcome.CannotWrite, Message: $"The edited picture cannot be saved in “{(folder.Length == 0 ? paths.RootName : folder)}”: {ex.Message}");
        }

        try
        {
            await indexer.IndexFilesAsync([target], cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The file is there: the next scan indexes it.
            logger.LogWarning(ex, "Cannot index the edited picture {Path}", target);
        }

        var relative = paths.ToRelative(target)!;
        logger.LogInformation("Saved the edited version of {Source} as {Target}", source, relative);
        return new(EditedImageOutcome.Saved, new EditedImage(relative, size.Width, size.Height, buffer.Length));
    }

    /// <summary>
    /// Saves <paramref name="content"/> over the picture <paramref name="path"/> itself, in its own format only, if it
    /// is still the version <paramref name="ifMatch"/>; the previous version goes to the trash at the first save of an
    /// editing <paramref name="session"/> (<see cref="FileContent.OverwriteAsync"/>). The routes check the rights.
    /// </summary>
    public async Task<EditedImageResult> SaveOverAsync(
        string? path, Stream content, string ifMatch, string session, bool keepPrevious, FileActor actor, CancellationToken cancellationToken = default)
    {
        if (Source(path) is not { } found)
            return new(EditedImageOutcome.SourceNotFound, Message: "The picture was not found.");
        var (source, full) = found;
        using var buffer = new MemoryStream();
        if (await ReadAsync(content, buffer, cancellationToken) is { } problem)
            return problem;
        var (format, size) = (Detect(buffer)!.Value, ReadSize(buffer)!.Value);
        if (!KeepsExtension(Path.GetExtension(full), format))
            return new(EditedImageOutcome.OtherFormat, Message: "The edited picture is not in the original's format: save a copy instead.");

        var written = await files.OverwriteAsync(source, buffer.ToArray(), ifMatch, session, keepPrevious, actor, cancellationToken);
        if (written.Status != OverwriteStatus.Done)
            return new(EditedImageOutcome.NotWritten, Message: written.Message, Written: written);
        logger.LogInformation("Saved the edited picture over {Path}", source);
        return new(EditedImageOutcome.Saved, new EditedImage(source, size.Width, size.Height, buffer.Length), Written: written);
    }

    /// <summary>The picture can be saved over with what the editor makes: JPEG, PNG and WebP files.</summary>
    public static bool SavesInPlace(string name) =>
        Path.GetExtension(name).TrimStart('.').ToLowerInvariant() is "jpg" or "jpeg" or "jpe" or "png" or "webp";

    /// <summary>The picture <paramref name="path"/> of the library: its canonical path and where it is; null when it isn't one.</summary>
    private (string Path, string Full)? Source(string? path)
    {
        var source = LibraryPaths.NormalizeRelative(path);
        if (string.IsNullOrEmpty(source) || !paths.TryResolve(source, out var full) || !File.Exists(full)
            || classifier.Classify(Path.GetExtension(full)) != ItemKind.Image)
            return null;
        return (paths.ToRelative(full) ?? source, full);
    }

    /// <summary>Reads what was sent into <paramref name="buffer"/>: the problem when it is too large or not a picture of a known size.</summary>
    private static async Task<EditedImageResult?> ReadAsync(Stream content, MemoryStream buffer, CancellationToken cancellationToken)
    {
        if (!await CopyLimitedAsync(content, buffer, cancellationToken))
            return new(EditedImageOutcome.TooLarge, Message: $"The edited picture is larger than {MaxBytes / 1024 / 1024} MB.");
        if (Detect(buffer) is null || ReadSize(buffer) is not { IsKnown: true })
            return new(EditedImageOutcome.InvalidImage, Message: "The edited picture is not a JPEG, PNG or WebP image.");
        return null;
    }

    /// <summary>
    /// The folder the edited version of <paramref name="source"/> goes to: the original's folders under the output
    /// folder; the original's own folder when the setting is empty or the original is already in the output folder
    /// (editing an edited picture again).
    /// </summary>
    public string? TargetFolder(string source)
    {
        var sourceFolder = ParentOf(source);
        var configured = options.CurrentValue.Editor.OutputFolder;
        if (string.IsNullOrWhiteSpace(configured))
            return sourceFolder;

        var output = LibraryPaths.NormalizeRelative(configured.Trim());
        if (output is null)
            return null;
        if (output.Length == 0 || IsWithin(sourceFolder, output))
            return sourceFolder;
        return sourceFolder.Length == 0 ? output : $"{output}/{sourceFolder}";
    }

    /// <summary>The name without extension: the original's, marked when it is saved next to it.</summary>
    private static string BaseName(string source, string folder)
    {
        var name = Path.GetFileNameWithoutExtension(source.Split('/')[^1]);
        return string.Equals(folder, ParentOf(source), LibraryPaths.PathComparison) ? name + EditedSuffix : name;
    }

    /// <summary>The original's extension when it names the format (<c>.JPEG</c> stays), else the format's.</summary>
    private static string Extension(string sourceFull, EditedImageFormat format)
    {
        var original = Path.GetExtension(sourceFull);
        return KeepsExtension(original, format) ? original : format switch
        {
            EditedImageFormat.Jpeg => ".jpg",
            EditedImageFormat.Png => ".png",
            _ => ".webp",
        };
    }

    /// <summary>The extension names the format.</summary>
    private static bool KeepsExtension(string extension, EditedImageFormat format) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "jpg" or "jpeg" or "jpe" => format == EditedImageFormat.Jpeg,
        "png" => format == EditedImageFormat.Png,
        "webp" => format == EditedImageFormat.WebP,
        _ => false,
    };

    /// <summary>Writes to the first free name; <see cref="FileMode.CreateNew"/> keeps two saves at once from sharing one.</summary>
    private static async Task<string> WriteNewAsync(string folder, string baseName, string extension, MemoryStream content, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxNameAttempts; attempt++)
        {
            var path = Path.Combine(folder, (attempt == 1 ? baseName : $"{baseName} ({attempt})") + extension);
            if (File.Exists(path) || Directory.Exists(path))
                continue;

            FileStream file;
            try
            {
                file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (File.Exists(path))
            {
                continue;
            }

            try
            {
                await using (file)
                {
                    content.Position = 0;
                    await content.CopyToAsync(file, cancellationToken);
                }
            }
            catch
            {
                // No half-written picture left behind.
                File.Delete(path);
                throw;
            }

            return path;
        }

        throw new IOException($"No free name for {baseName}{extension}.");
    }

    private static async Task<bool> CopyLimitedAsync(Stream content, MemoryStream buffer, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
                return false;
            buffer.Write(chunk, 0, read);
        }

        return true;
    }

    internal static EditedImageFormat? Detect(MemoryStream content)
    {
        var head = content.GetBuffer().AsSpan(0, (int)Math.Min(content.Length, 16));
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            return EditedImageFormat.Jpeg;
        if (head.Length >= 8 && head[..8].SequenceEqual<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return EditedImageFormat.Png;
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
            return EditedImageFormat.WebP;
        return null;
    }

    private static PixelSize? ReadSize(MemoryStream content)
    {
        content.Position = 0;
        try
        {
            return MediaDimensions.ReadImageHeader(content);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or IndexOutOfRangeException or EndOfStreamException)
        {
            return null;
        }
    }

    private static string ParentOf(string path) => path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : string.Empty;

    private static bool IsWithin(string folder, string ancestor) =>
        string.Equals(folder, ancestor, LibraryPaths.PathComparison) || folder.StartsWith(ancestor + "/", LibraryPaths.PathComparison);
}
