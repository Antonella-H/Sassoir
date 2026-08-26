using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Sassoir.Api.Data;
using Sassoir.Api.Models;

namespace Sassoir.Api.Endpoints;

public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/uploads/event-image", async (HttpRequest request, AuthStore auth) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            if (!request.HasFormContentType) return Results.BadRequest(new { message = "Upload a multipart form file." });

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.BadRequest(new { message = "Image file is required." });
            if (file.Length > 5 * 1024 * 1024) return Results.BadRequest(new { message = "Image must be 5MB or smaller." });

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            if (!allowedExtensions.Contains(extension)) return Results.BadRequest(new { message = "Use a JPG, PNG, WebP, or GIF image." });

            var contentType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                _ => file.ContentType
            };

            await using var memory = new MemoryStream();
            await file.CopyToAsync(memory);
            var dataUrl = $"data:{contentType};base64,{Convert.ToBase64String(memory.ToArray())}";

            return Results.Ok(new UploadResponse(dataUrl));
        }).DisableAntiforgery();

        return app;
    }

}