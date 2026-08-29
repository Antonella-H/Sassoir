using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Sassoir.Api.Data;
using Sassoir.Api.Models;

namespace Sassoir.Api.Endpoints;

public static class AdminEventEndpoints
{
    public static IEndpointRouteBuilder MapAdminEventEndpoints(this IEndpointRouteBuilder app)
    {
        var adminApi = app.MapGroup("/api/admin")
            .WithTags("Admin API");

        adminApi.MapGet("/events", (HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            return Results.Ok(store.GetAdminEvents());
        });

        adminApi.MapGet("/events/page", async (HttpRequest request, AuthStore auth, EventStore store, string? search, string? status, int? page, int? pageSize, CancellationToken cancellationToken) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            return Results.Ok(await store.GetAdminEventsPageAsync(search, status, page, pageSize, cancellationToken));
        });

        adminApi.MapGet("/events/{id:guid}/guests/page", async (Guid id, HttpRequest request, AuthStore auth, EventStore store, string? search, string? status, string? tableId, int? page, int? pageSize, CancellationToken cancellationToken) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            return Results.Ok(await store.GetAdminGuestsPageAsync(id, search, status, tableId, page, pageSize, cancellationToken));
        });

        adminApi.MapGet("/events/{id:guid}/tables/page", async (Guid id, HttpRequest request, AuthStore auth, EventStore store, string? search, int? page, int? pageSize, CancellationToken cancellationToken) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            return Results.Ok(await store.GetAdminTablesPageAsync(id, search, page, pageSize, cancellationToken));
        });

        adminApi.MapGet("/events/{id:guid}/messages/page", async (Guid id, HttpRequest request, AuthStore auth, EventStore store, int? page, int? pageSize, CancellationToken cancellationToken) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            return Results.Ok(await store.GetGuestMessagesPageAsync(id, page, pageSize, cancellationToken));
        });

        app.MapGet("/api/admin/events/{id:guid}", async (Guid id, HttpRequest request, AuthStore auth, EventStore store, CancellationToken cancellationToken) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();

            var eventDetails = await store.GetAdminEventAsync(id, cancellationToken);
            return eventDetails is null ? Results.NotFound() : Results.Ok(eventDetails);
        });

        app.MapPost("/api/admin/events", (AdminEventUpsertRequest request, HttpRequest httpRequest, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(httpRequest)) return Results.Unauthorized();

            var validation = AdminEventValidator.Validate(request);
            if (validation.Count > 0) return Results.ValidationProblem(validation);

            var result = store.CreateEvent(request);
            return result.Error is not null
                ? Results.Conflict(new { message = result.Error })
                : Results.Created($"/api/admin/events/{result.Event!.Id}", result.Event.ToAdminDto());
        });

        app.MapPut("/api/admin/events/{id:guid}", (Guid id, AdminEventUpsertRequest request, HttpRequest httpRequest, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(httpRequest)) return Results.Unauthorized();

            var validation = AdminEventValidator.Validate(request);
            if (validation.Count > 0) return Results.ValidationProblem(validation);

            var result = store.UpdateEvent(id, request);
            return result.Error switch
            {
                "not-found" => Results.NotFound(),
                not null => Results.Conflict(new { message = result.Error }),
                _ => Results.Ok(result.Event!.ToAdminDto())
            };
        });

        app.MapDelete("/api/admin/events/{id:guid}", (Guid id, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();

            return store.DeleteEvent(id) ? Results.NoContent() : Results.NotFound();
        });

        app.MapPost("/api/admin/events/{id:guid}/publish", (Guid id, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();

            var result = store.SetEventStatus(id, EventStatus.Published);
            return result is null ? Results.NotFound() : Results.Ok(result.ToAdminDto());
        });

        app.MapPost("/api/admin/events/{id:guid}/unpublish", (Guid id, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();

            var result = store.SetEventStatus(id, EventStatus.Draft);
            return result is null ? Results.NotFound() : Results.Ok(result.ToAdminDto());
        });

        app.MapGet("/api/admin/events/{id:guid}/guests", (Guid id, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            return store.GetEvent(id) is null ? Results.NotFound() : Results.Ok(store.GetAdminGuests(id));
        });

        app.MapPost("/api/admin/events/{id:guid}/guests", (Guid id, AdminGuestCreateRequest guest, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(guest.FirstName) && string.IsNullOrWhiteSpace(guest.DisplayName))
            {
                return Results.BadRequest(new { message = "First name or display name is required." });
            }

            var result = store.CreateGuest(id, guest);
            return result.Error switch
            {
                "not-found" => Results.NotFound(),
                not null => Results.BadRequest(new { message = result.Error }),
                _ => Results.Created($"/api/admin/events/{id}/guests/{result.Guest!.Id}", result.Guest)
            };
        });

        app.MapPut("/api/admin/events/{eventId:guid}/guests/{guestId:guid}", (Guid eventId, Guid guestId, AdminGuestUpsertRequest guest, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(guest.FirstName) && string.IsNullOrWhiteSpace(guest.DisplayName))
            {
                return Results.BadRequest(new { message = "First name or display name is required." });
            }

            var result = store.UpdateGuest(eventId, guestId, guest);
            return result.Error switch
            {
                "not-found" => Results.NotFound(),
                not null => Results.BadRequest(new { message = result.Error }),
                _ => Results.Ok(result.Guest)
            };
        });

        app.MapPost("/api/admin/events/{eventId:guid}/guests/{guestId:guid}/archive", (Guid eventId, Guid guestId, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();

            var result = store.ArchiveGuest(eventId, guestId);
            return result.Error switch
            {
                "not-found" => Results.NotFound(),
                not null => Results.BadRequest(new { message = result.Error }),
                _ => Results.Ok(result.Guest)
            };
        });

        app.MapDelete("/api/admin/events/{eventId:guid}/guests/{guestId:guid}", (Guid eventId, Guid guestId, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();

            return store.DeleteGuest(eventId, guestId) ? Results.NoContent() : Results.NotFound();
        });

        app.MapPost("/api/admin/events/{eventId:guid}/guests/bulk-delete", (Guid eventId, BulkGuestRequest deleteRequest, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();

            var result = store.BulkDeleteGuests(eventId, deleteRequest.GuestIds);
            return result.Error switch
            {
                "not-found" => Results.NotFound(),
                not null => Results.BadRequest(new { message = result.Error }),
                _ => Results.Ok(new { deletedCount = result.DeletedCount })
            };
        });

        app.MapPost("/api/admin/events/{eventId:guid}/guests/{guestId:guid}/assign-table", (Guid eventId, Guid guestId, AssignGuestTableRequest assignment, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();

            var result = store.AssignGuestToTable(eventId, guestId, assignment.TableId, assignment.SeatNumber);
            return result.Error switch
            {
                "not-found" => Results.NotFound(),
                not null => Results.BadRequest(new { message = result.Error }),
                _ => Results.Ok(result.Guest)
            };
        });

        app.MapPost("/api/admin/events/{eventId:guid}/guests/bulk-assign-table", (Guid eventId, BulkAssignGuestTableRequest assignment, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();

            var result = store.BulkAssignGuestsToTable(eventId, assignment.GuestIds, assignment.TableId);
            return result.Error switch
            {
                "not-found" => Results.NotFound(),
                not null => Results.BadRequest(new { message = result.Error }),
                _ => Results.Ok(result.Guests)
            };
        });

        app.MapPost("/api/admin/events/{id:guid}/guests/import/preview", (Guid id, AdminGuestImportRequest import, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();

            var result = store.PreviewGuestImport(id, import.Guests);
            return result.Error switch
            {
                "not-found" => Results.NotFound(),
                not null => Results.BadRequest(new { message = result.Error }),
                _ => Results.Ok(result.Preview)
            };
        });

        app.MapPost("/api/admin/events/{id:guid}/guests/import/preview-csv", async (Guid id, HttpRequest request, AuthStore auth, EventStore store, CancellationToken cancellationToken) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();

            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var csv = await reader.ReadToEndAsync(cancellationToken);
            var result = store.PreviewGuestImportCsv(id, csv);
            return result.Error switch
            {
                "not-found" => Results.NotFound(),
                not null => Results.BadRequest(new { message = result.Error }),
                _ => Results.Ok(result.Preview)
            };
        });

        app.MapPost("/api/admin/events/{id:guid}/guests/import/commit", (Guid id, AdminGuestImportRequest import, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();

            var result = store.ImportGuests(id, import.Guests);
            return result.Error switch
            {
                "not-found" => Results.NotFound(),
                not null => Results.BadRequest(new { message = result.Error }),
                _ => Results.Created($"/api/admin/events/{id}/guests", result.Guests)
            };
        });

        app.MapPost("/api/admin/events/{id:guid}/guests/import/commit-csv", async (Guid id, HttpRequest request, AuthStore auth, EventStore store, CancellationToken cancellationToken) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();

            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var csv = await reader.ReadToEndAsync(cancellationToken);
            var result = store.ImportGuestsCsv(id, csv);
            return result.Error switch
            {
                "not-found" => Results.NotFound(),
                not null => Results.BadRequest(new { message = result.Error }),
                _ => Results.Ok(result.Guests)
            };
        });

        app.MapGet("/api/admin/events/{id:guid}/guests/export", (Guid id, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            if (store.GetEvent(id) is null) return Results.NotFound();

            var csv = store.ExportGuestsCsv(id);
            return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv", $"sassoir-guests-{id:N}.csv");
        });

        app.MapGet("/api/admin/events/{id:guid}/tables", (Guid id, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            return store.GetEvent(id) is null ? Results.NotFound() : Results.Ok(store.GetAdminTables(id));
        });

        app.MapPost("/api/admin/events/{id:guid}/tables", (Guid id, AdminTableCreateRequest table, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(table.Name) || string.IsNullOrWhiteSpace(table.Number))
            {
                return Results.BadRequest(new { message = "Table name and number are required." });
            }

            var result = store.CreateTable(id, table);
            return result.Error switch
            {
                "not-found" => Results.NotFound(),
                not null => Results.BadRequest(new { message = result.Error }),
                _ => Results.Created($"/api/admin/events/{id}/tables/{result.Table!.Id}", result.Table)
            };
        });

        app.MapPut("/api/admin/events/{eventId:guid}/tables/{tableId:guid}", (Guid eventId, Guid tableId, AdminTableUpsertRequest table, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(table.Name) || string.IsNullOrWhiteSpace(table.Number))
            {
                return Results.BadRequest(new { message = "Table name and number are required." });
            }

            var result = store.UpdateTable(eventId, tableId, table);
            return result.Error switch
            {
                "not-found" => Results.NotFound(),
                not null => Results.BadRequest(new { message = result.Error }),
                _ => Results.Ok(result.Table)
            };
        });

        app.MapDelete("/api/admin/events/{eventId:guid}/tables/{tableId:guid}", (Guid eventId, Guid tableId, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            return store.DeleteTable(eventId, tableId) ? Results.NoContent() : Results.NotFound();
        });

        app.MapGet("/api/admin/events/{id:guid}/messages", (Guid id, HttpRequest request, AuthStore auth, EventStore store) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            return store.GetEvent(id) is null ? Results.NotFound() : Results.Ok(store.GetGuestMessages(id));
        });

        app.MapGet("/api/admin/events/{id:guid}/floor-plan", async (Guid id, HttpRequest request, AuthStore auth, EventStore store, CancellationToken cancellationToken) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            var floorPlan = await store.GetAdminFloorPlanAsync(id, cancellationToken);
            return floorPlan is null ? Results.NotFound() : Results.Ok(floorPlan);
        });

        app.MapPut("/api/admin/events/{id:guid}/floor-plan", async (Guid id, FloorPlanSaveRequest floorPlan, HttpRequest request, AuthStore auth, EventStore store, CancellationToken cancellationToken) =>
        {
            if (!auth.IsAdmin(request)) return Results.Unauthorized();
            var result = await store.SaveFloorPlanAsync(id, floorPlan, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        return app;
    }

}
