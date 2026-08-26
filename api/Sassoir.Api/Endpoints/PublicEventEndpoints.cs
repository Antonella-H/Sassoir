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

        publicApi.MapPost("/{slug}/guests/{publicToken}/messages", async (string slug, string publicToken, GuestMessageRequest request, EventStore store, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message)) return Results.BadRequest(new { message = "Message is required." });

            var saved = await store.SaveMessageAsync(slug, publicToken, request.Message.Trim(), cancellationToken);
            if (!saved) return Results.NotFound();

            return Results.Created($"/api/public/events/{slug}/guests/{publicToken}/messages", new { status = "saved" });
        }).RequireRateLimiting("PublicMessage");

        return app;
    }

}