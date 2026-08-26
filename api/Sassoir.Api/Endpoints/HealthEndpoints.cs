using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Sassoir.Api.Data;
using Sassoir.Api.Models;

namespace Sassoir.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "ok",
            service = "sassoir-api",
            time = DateTimeOffset.UtcNow
        }));
        app.MapGet("/api/health/live", () => Results.Ok(new { status = "live", time = DateTimeOffset.UtcNow }));
        app.MapGet("/api/health/ready", async (SassoirDbContext db, CancellationToken cancellationToken) =>
        {
            var databaseReady = await db.Database.CanConnectAsync(cancellationToken);
            return databaseReady
                ? Results.Ok(new { status = "ready", database = "ok", time = DateTimeOffset.UtcNow })
                : Results.Problem("Database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        return app;
    }

}