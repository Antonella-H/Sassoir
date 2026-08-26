using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Sassoir.Api.Data;
using Sassoir.Api.Models;

namespace Sassoir.Api.Endpoints;

public static class ContactEndpoints
{
    public static IEndpointRouteBuilder MapContactEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/contact", async (ContactSubmissionRequest submission, SassoirDbContext db, CancellationToken cancellationToken) =>
        {
            var name = (submission.Name ?? string.Empty).Trim();
            var email = (submission.Email ?? string.Empty).Trim();
            var message = (submission.Message ?? string.Empty).Trim();
            var validation = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(name)) validation["name"] = ["Name is required."];
            if (string.IsNullOrWhiteSpace(email)) validation["email"] = ["Email is required."];
            else if (!ContactEmailIsValid(email)) validation["email"] = ["Use a valid email address."];
            if (string.IsNullOrWhiteSpace(message)) validation["message"] = ["Message is required."];

            if (validation.Count > 0) return Results.ValidationProblem(validation);

            var entity = new ContactSubmissionEntity
            {
                Id = Guid.NewGuid(),
                Name = name,
                Email = email,
                Message = message,
                SubmittedAtUtc = DateTimeOffset.UtcNow
            };

            db.ContactSubmissions.Add(entity);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/contact/{entity.Id}", new ContactSubmissionDto(entity.Id, entity.Name, entity.Email, entity.Message, entity.SubmittedAtUtc));
        });

        app.MapGet("/api/contact", async (HttpRequest request, AuthStore auth, SassoirDbContext db, int? page, int? pageSize, CancellationToken cancellationToken) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();

            var resolvedPage = Math.Max(1, page ?? 1);
            var resolvedPageSize = Math.Clamp(pageSize ?? 20, 1, 100);
            var query = db.ContactSubmissions.AsNoTracking().OrderByDescending(item => item.SubmittedAtUtc);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip((resolvedPage - 1) * resolvedPageSize)
                .Take(resolvedPageSize)
                .Select(item => new ContactSubmissionDto(item.Id, item.Name, item.Email, item.Message, item.SubmittedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new PaginatedResponse<ContactSubmissionDto>(items, resolvedPage, resolvedPageSize, totalCount));
        });

        return app;
    }


    private static bool ContactEmailIsValid(string email)
    {
        try
        {
            _ = new System.Net.Mail.MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }
}