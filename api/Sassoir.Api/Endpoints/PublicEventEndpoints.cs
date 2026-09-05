using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Sassoir.Api.Data;
using Sassoir.Api.Models;

namespace Sassoir.Api.Endpoints;

public static class PublicEventEndpoints
{
    public static IEndpointRouteBuilder MapPublicEventEndpoints(this IEndpointRouteBuilder app)
    {
        var publicApi = app.MapGroup("/api/public/events")
            .WithTags("Public API");

        publicApi.MapGet("/{slug}", async (string slug, EventStore store, CancellationToken cancellationToken) =>
        {
            var eventDetails = await store.GetPublishedPublicEventAsync(slug, cancellationToken);
            return eventDetails is null ? Results.NotFound() : Results.Ok(eventDetails);
        }).RequireRateLimiting("PublicEvent");

        publicApi.MapGet("/{slug}/floor-plan", async (string slug, EventStore store, CancellationToken cancellationToken) =>
        {
            var floorPlan = await store.GetPublishedFloorPlanAsync(slug, cancellationToken);
            return floorPlan is null ? Results.NotFound() : Results.Ok(floorPlan);
        }).RequireRateLimiting("PublicEvent");

        publicApi.MapPost("/{slug}/guests/search", async (string slug, GuestSearchRequest request, EventStore store, CancellationToken cancellationToken) =>
        {
            var query = SearchNormalizer.Normalize(request.Query);
            if (query.Length < 2)
            {
                return Results.Ok(new GuestSearchResponse([]));
            }

            var results = await store.SearchPublicGuestsAsync(slug, query, cancellationToken);

            await store.TrackSearchAsync(slug, query, results.Length > 0, cancellationToken);
            return Results.Ok(new GuestSearchResponse(results));
        }).RequireRateLimiting("PublicSearch");

        publicApi.MapGet("/{slug}/guests/{publicToken}", async (string slug, string publicToken, EventStore store, CancellationToken cancellationToken) =>
        {
            var guest = await store.GetPublicGuestSeatAsync(slug, publicToken, cancellationToken);

            return guest is null
                ? Results.NotFound()
                : Results.Ok(guest);
        }).RequireRateLimiting("PublicSeat");

        publicApi.MapGet("/{slug}/guests/{publicToken}/floor-plan", async (string slug, string publicToken, EventStore store, CancellationToken cancellationToken) =>
        {
            var floorPlan = await store.GetPublicGuestFloorPlanAsync(slug, publicToken, cancellationToken);

            return floorPlan is null
                ? Results.NotFound()
                : Results.Ok(floorPlan);
        }).RequireRateLimiting("PublicSeat");

        publicApi.MapGet("/{slug}/song-requests", async (string slug, EventStore store, int? pageSize, CancellationToken cancellationToken) =>
        {
            var requests = await store.GetPublicSongRequestsPageAsync(slug, pageSize, cancellationToken);

            return requests is null
                ? Results.NotFound()
                : Results.Ok(requests);
        }).RequireRateLimiting("PublicEvent");

        publicApi.MapGet("/{slug}/dj/{djAccessToken}/song-requests", async (string slug, string djAccessToken, EventStore store, int? page, int? pageSize, CancellationToken cancellationToken) =>
        {
            var requests = await store.GetDjSongRequestsPageAsync(slug, djAccessToken, page, pageSize, cancellationToken);

            return requests is null
                ? Results.NotFound()
                : Results.Ok(requests);
        }).RequireRateLimiting("PublicEvent");

        publicApi.MapPost("/{slug}/guests/{publicToken}/messages", async (string slug, string publicToken, GuestMessageRequest request, EventStore store, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message)) return Results.BadRequest(new { message = "Message is required." });

            var saved = await store.SaveMessageAsync(slug, publicToken, request.Message.Trim(), cancellationToken);
            if (!saved) return Results.NotFound();

            return Results.Created($"/api/public/events/{slug}/guests/{publicToken}/messages", new { status = "saved" });
        }).RequireRateLimiting("PublicMessage");

        publicApi.MapPost("/{slug}/guests/{publicToken}/song-requests", async (string slug, string publicToken, SongRequestCreateRequest request, EventStore store, CancellationToken cancellationToken) =>
        {
            var songTitle = (request.SongTitle ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(songTitle)) return Results.BadRequest(new { message = "Song title is required." });
            if (songTitle.Length > 200) return Results.BadRequest(new { message = "Song title must be 200 characters or fewer." });

            var saved = await store.SaveSongRequestAsync(slug, publicToken, songTitle, cancellationToken);
            if (saved is null) return Results.NotFound();

            return Results.Created($"/api/public/events/{slug}/song-requests/{saved.Id}", saved);
        }).RequireRateLimiting("PublicSongRequest");

        return app;
    }

}
