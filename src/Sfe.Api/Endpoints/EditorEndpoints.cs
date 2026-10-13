using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Editing;

namespace Sfe.Api.Endpoints;

public static class EditorEndpoints
{
    public static RouteGroupBuilder MapEditorEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/editor/images", SaveImage)
            .RequireRight(Rights.EditImages)
            // Room for a large lossless picture (Kestrel's default stops at 30 MB); the service checks the size again.
            .WithMetadata(new RequestSizeLimitAttribute(EditedImages.MaxBytes + 1))
            .WithName("SaveEditedImage")
            .WithTags("Editor")
            .WithSummary("Saves an edited picture as a new file of the library; the original is never changed.")
            .WithDescription($"""
                The body is the edited picture itself, as the web client's editor encoded it: JPEG, PNG or WebP (at most
                {EditedImages.MaxBytes / 1024 / 1024} MB; the format is read from its bytes, not from `Content-Type`).
                `path` is the original. The new file goes to `Editor:OutputFolder` under the original's own folders
                (`Photos/2024/beach.jpg` → `Edited/Photos/2024/beach.jpg`), or next to the original as
                `beach (edited).jpg` when the setting is empty or the original is already in that folder. An existing
                name gets ` (2)`, ` (3)`... The file is indexed at once. Answers 201 with its path and size in pixels.
                """);

        return api;
    }

    private static async Task<Results<Created<EditedImage>, ProblemHttpResult>> SaveImage(
        EditedImages editor, HttpRequest request, CancellationToken cancellationToken, string? path = null)
    {
        var result = await editor.SaveAsync(path, request.Body, cancellationToken);
        return result.Outcome switch
        {
            EditedImageOutcome.Saved => TypedResults.Created((string?)null, result.Image!),
            EditedImageOutcome.SourceNotFound => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status404NotFound),
            EditedImageOutcome.TooLarge => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status413PayloadTooLarge),
            EditedImageOutcome.InvalidImage => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status400BadRequest),
            // Not a 409: the client takes those as answers carrying the current state.
            _ => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status500InternalServerError),
        };
    }
}
