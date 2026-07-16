using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Sassoir.Api.Data;
using Sassoir.Api.Models;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .GetChildren()
    .Select(origin => origin.Value)
    .Concat((builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin!.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (allowedOrigins is null || allowedOrigins.Length == 0)
{
    allowedOrigins = ["http://127.0.0.1:5173", "http://localhost:5173"];
}

builder.Services.AddDbContext<SassoirDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});
builder.Services.AddScoped<EventStore>();
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddScoped<AuthStore>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredWebOrigins", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors("ConfiguredWebOrigins");

var configuredUploadRoot = app.Configuration["Uploads:RootPath"];
var uploadRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredUploadRoot)
    ? Path.Combine(app.Environment.ContentRootPath, "uploads")
    : configuredUploadRoot);
Directory.CreateDirectory(uploadRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadRoot),
    RequestPath = "/api/uploads"
});

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "sassoir-api",
    time = DateTimeOffset.UtcNow
}));

app.MapPost("/api/auth/login", (LoginRequest request, AuthStore auth) =>
{
    var login = auth.Login(request.Email, request.Password);
    return login is null ? Results.Unauthorized() : Results.Ok(login);
});

app.MapGet("/api/auth/me", (HttpRequest request, AuthStore auth) =>
{
    var user = auth.GetCurrentUser(request);
    return user is null ? Results.Unauthorized() : Results.Ok(user);
});

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

    var fileName = $"{Guid.NewGuid():N}{extension}";
    var eventUploadRoot = Path.Combine(uploadRoot, "events");
    Directory.CreateDirectory(eventUploadRoot);
    var filePath = Path.Combine(eventUploadRoot, fileName);

    await using var stream = File.Create(filePath);
    await file.CopyToAsync(stream);

    return Results.Ok(new UploadResponse($"/api/uploads/events/{fileName}"));
}).DisableAntiforgery();

app.MapGet("/api/public/events/{slug}", (string slug, EventStore store) =>
{
    var eventDetails = store.GetPublishedEvent(slug);
    return eventDetails is null ? Results.NotFound() : Results.Ok(eventDetails.ToPublicDto());
});

app.MapGet("/api/public/events/{slug}/floor-plan", (string slug, EventStore store) =>
{
    var eventDetails = store.GetPublishedEvent(slug);
    return eventDetails is null ? Results.NotFound() : Results.Ok(eventDetails.FloorPlan);
});

app.MapPost("/api/public/events/{slug}/guests/search", (string slug, GuestSearchRequest request, EventStore store) =>
{
    var eventDetails = store.GetPublishedEvent(slug);
    if (eventDetails is null) return Results.NotFound();

    var query = SearchNormalizer.Normalize(request.Query);
    if (query.Length < 2)
    {
        return Results.Ok(new GuestSearchResponse([]));
    }

    var results = eventDetails.Guests
        .Where(guest => guest.Status == GuestStatus.Active)
        .Select(guest => new
        {
            Guest = guest,
            Rank = SearchNormalizer.Rank(guest, query)
        })
        .Where(match => match.Rank < 99)
        .OrderBy(match => match.Rank)
        .ThenBy(match => match.Guest.DisplayName)
        .Take(5)
        .Select(match => match.Guest.ToSearchDto())
        .ToArray();

    store.TrackSearch(slug, query, results.Length > 0);
    return Results.Ok(new GuestSearchResponse(results));
});

app.MapGet("/api/public/events/{slug}/guests/{publicToken}", (string slug, string publicToken, EventStore store) =>
{
    var eventDetails = store.GetPublishedEvent(slug);
    var guest = eventDetails?.Guests.SingleOrDefault(item => item.PublicToken == publicToken);

    return guest is null || eventDetails is null
        ? Results.NotFound()
        : Results.Ok(guest.ToSeatDto(eventDetails));
});

app.MapGet("/api/public/events/{slug}/guests/{publicToken}/floor-plan", (string slug, string publicToken, EventStore store) =>
{
    var eventDetails = store.GetPublishedEvent(slug);
    var guest = eventDetails?.Guests.SingleOrDefault(item => item.PublicToken == publicToken);

    return guest is null || eventDetails is null
        ? Results.NotFound()
        : Results.Ok(new GuestFloorPlanDto(eventDetails.FloorPlan, $"table-{guest.TableCode}"));
});

app.MapPost("/api/public/events/{slug}/guests/{publicToken}/messages", (string slug, string publicToken, GuestMessageRequest request, EventStore store) =>
{
    var eventDetails = store.GetPublishedEvent(slug);
    var guest = eventDetails?.Guests.SingleOrDefault(item => item.PublicToken == publicToken);

    if (guest is null || eventDetails is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(request.Message)) return Results.BadRequest(new { message = "Message is required." });

    store.SaveMessage(slug, publicToken, request.Message.Trim());
    return Results.Created($"/api/public/events/{slug}/guests/{publicToken}/messages", new { status = "saved" });
});

app.MapGet("/api/admin/events", (HttpRequest request, AuthStore auth, EventStore store) =>
{
    if (!auth.IsAdmin(request)) return Results.Unauthorized();
    return Results.Ok(store.GetAdminEvents());
});

app.MapGet("/api/admin/events/{id:guid}", (Guid id, HttpRequest request, AuthStore auth, EventStore store) =>
{
    if (!auth.IsAdmin(request)) return Results.Unauthorized();

    var eventDetails = store.GetEvent(id);
    return eventDetails is null ? Results.NotFound() : Results.Ok(eventDetails.ToAdminDto());
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

app.MapPost("/api/admin/events/{eventId:guid}/guests/{guestId:guid}/assign-table", (Guid eventId, Guid guestId, AssignGuestTableRequest assignment, HttpRequest request, AuthStore auth, EventStore store) =>
{
    if (!auth.IsAdmin(request)) return Results.Unauthorized();

    var result = store.AssignGuestToTable(eventId, guestId, assignment.TableId);
    return result.Error switch
    {
        "not-found" => Results.NotFound(),
        not null => Results.BadRequest(new { message = result.Error }),
        _ => Results.Ok(result.Guest)
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

app.MapGet("/api/admin/events/{id:guid}/floor-plan", (Guid id, HttpRequest request, AuthStore auth, EventStore store) =>
{
    if (!auth.IsAdmin(request)) return Results.Unauthorized();
    var eventDetails = store.GetEvent(id);
    return eventDetails is null ? Results.NotFound() : Results.Ok(eventDetails.FloorPlan);
});

app.MapPut("/api/admin/events/{id:guid}/floor-plan", (Guid id, FloorPlanSaveRequest floorPlan, HttpRequest request, AuthStore auth, EventStore store) =>
{
    if (!auth.IsAdmin(request)) return Results.Unauthorized();
    var result = store.SaveFloorPlan(id, floorPlan);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.Run();

namespace Sassoir.Api.Data
{
    public sealed class EventStore
    {
        private readonly SassoirDbContext _db;

        public EventStore(SassoirDbContext db)
        {
            _db = db;
            EnsureEventContentSchema();
        }

        public IReadOnlyList<EventDetails> Events => EventQuery()
            .OrderByDescending(item => item.CreatedAt)
            .AsEnumerable()
            .Select(ToEventDetails)
            .ToArray();

        public EventDetails? GetPublishedEvent(string slug)
        {
            var eventEntity = EventQuery()
                .SingleOrDefault(item => item.Slug.ToLower() == slug.ToLower() && item.Status == EventStatus.Published);

            return eventEntity is null ? null : ToEventDetails(eventEntity);
        }

        public EventDetails? GetEvent(Guid id)
        {
            var eventEntity = EventQuery().SingleOrDefault(item => item.Id == id);
            return eventEntity is null ? null : ToEventDetails(eventEntity);
        }

        public IReadOnlyList<AdminEventDto> GetAdminEvents()
        {
            return _db.Events
                .AsNoTracking()
                .Include(item => item.Theme)
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new AdminEventDto(
                    item.Id,
                    item.Name,
                    item.Slug,
                    item.EventType,
                    item.Subtitle,
                    item.DateLabel,
                    item.VenueName,
                    item.VenueAddress,
                    item.Status,
                    item.Theme == null ? string.Empty : item.Theme.HeroText,
                    item.Theme == null ? "#D8CFBC" : item.Theme.PrimaryColor,
                    item.Theme == null ? "#565449" : item.Theme.SecondaryColor,
                    item.Theme == null ? "#FFFBF4" : item.Theme.BackgroundColor,
                    item.Theme == null ? "#11120D" : item.Theme.TextColor,
                    item.Theme == null || item.Theme.WelcomeTitle == string.Empty ? $"Welcome to {item.Name}" : item.Theme.WelcomeTitle,
                    item.Theme == null || item.Theme.SearchInputLabel == string.Empty ? "Search by name" : item.Theme.SearchInputLabel,
                    item.Theme == null || item.Theme.SearchPlaceholder == string.Empty ? "Search by name" : item.Theme.SearchPlaceholder,
                    item.Theme == null ? null : item.Theme.HeroImageUrl,
                    item.Guests.Count(guest => guest.Status != GuestStatus.Archived),
                    item.Guests.Count(guest => guest.Status != GuestStatus.Archived && guest.TableId != null)))
                .ToArray();
        }

        public (EventDetails? Event, string? Error) CreateEvent(AdminEventUpsertRequest request)
        {
            var slug = request.Slug.Trim().ToLowerInvariant();
            if (_db.Events.Any(item => item.Slug.ToLower() == slug))
            {
                return (null, "An event with this slug already exists.");
            }

            var organization = GetOrCreateDemoOrganization();
            var now = DateTimeOffset.UtcNow;
            var eventEntity = new EventEntity
            {
                Id = Guid.NewGuid(),
                OrganizationId = organization.Id,
                Name = request.Name.Trim(),
                Slug = slug,
                EventType = NormalizeEventType(request.EventType),
                Subtitle = request.Subtitle?.Trim() ?? string.Empty,
                Description = string.Empty,
                DateLabel = request.DateLabel?.Trim() ?? string.Empty,
                VenueName = request.VenueName?.Trim() ?? string.Empty,
                VenueAddress = request.VenueAddress?.Trim() ?? string.Empty,
                Status = request.Status,
                IsPublic = request.Status == EventStatus.Published,
                PublishedAt = request.Status == EventStatus.Published ? now : null,
                CreatedAt = now,
                UpdatedAt = now,
                Theme = new EventThemeEntity
                {
                    Id = Guid.NewGuid(),
                    LogoText = BuildLogoText(request.Name),
                    HeroText = request.HeroText?.Trim() ?? "A polished guest seating experience.",
                    PrimaryColor = request.PrimaryColor?.Trim() ?? "#D8CFBC",
                    SecondaryColor = request.SecondaryColor?.Trim() ?? "#565449",
                    BackgroundColor = request.BackgroundColor?.Trim() ?? "#FFFBF4",
                    TextColor = request.TextColor?.Trim() ?? "#11120D",
                    WelcomeTitle = request.WelcomeTitle?.Trim() ?? $"Welcome to {request.Name.Trim()}",
                    SearchInputLabel = request.SearchInputLabel?.Trim() ?? "Search by name",
                    SearchPlaceholder = request.SearchPlaceholder?.Trim() ?? "Search by name",
                    HeroImageUrl = BlankToNull(request.HeroImageUrl),
                    UpdatedAt = now
                },
                FloorPlans =
                [
                    new FloorPlanEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = "New venue layout",
                        CanvasAspectRatio = 1.14m,
                        Version = 1,
                        IsActive = true,
                        CreatedAt = now
                    }
                ]
            };

            _db.Events.Add(eventEntity);
            _db.SaveChanges();
            return (GetEvent(eventEntity.Id), null);
        }

        public (EventDetails? Event, string? Error) UpdateEvent(Guid id, AdminEventUpsertRequest request)
        {
            var eventEntity = _db.Events
                .Include(item => item.Theme)
                .SingleOrDefault(item => item.Id == id);
            if (eventEntity is null) return (null, "not-found");

            var slug = request.Slug.Trim().ToLowerInvariant();
            if (_db.Events.Any(item => item.Id != id && item.Slug.ToLower() == slug))
            {
                return (null, "An event with this slug already exists.");
            }

            eventEntity.Name = request.Name.Trim();
            eventEntity.Slug = slug;
            eventEntity.EventType = NormalizeEventType(request.EventType);
            eventEntity.Subtitle = request.Subtitle?.Trim() ?? string.Empty;
            eventEntity.DateLabel = request.DateLabel?.Trim() ?? string.Empty;
            eventEntity.VenueName = request.VenueName?.Trim() ?? string.Empty;
            eventEntity.VenueAddress = request.VenueAddress?.Trim() ?? string.Empty;
            eventEntity.Status = request.Status;
            eventEntity.IsPublic = request.Status == EventStatus.Published;
            eventEntity.PublishedAt = request.Status == EventStatus.Published ? eventEntity.PublishedAt ?? DateTimeOffset.UtcNow : null;
            eventEntity.UpdatedAt = DateTimeOffset.UtcNow;

            eventEntity.Theme ??= new EventThemeEntity { Id = Guid.NewGuid(), EventId = eventEntity.Id };
            eventEntity.Theme.LogoText = BuildLogoText(request.Name);
            eventEntity.Theme.HeroText = request.HeroText?.Trim() ?? eventEntity.Theme.HeroText;
            eventEntity.Theme.PrimaryColor = request.PrimaryColor?.Trim() ?? eventEntity.Theme.PrimaryColor;
            eventEntity.Theme.SecondaryColor = request.SecondaryColor?.Trim() ?? eventEntity.Theme.SecondaryColor;
            eventEntity.Theme.BackgroundColor = request.BackgroundColor?.Trim() ?? eventEntity.Theme.BackgroundColor;
            eventEntity.Theme.TextColor = request.TextColor?.Trim() ?? eventEntity.Theme.TextColor;
            eventEntity.Theme.WelcomeTitle = request.WelcomeTitle?.Trim() ?? eventEntity.Theme.WelcomeTitle;
            eventEntity.Theme.SearchInputLabel = request.SearchInputLabel?.Trim() ?? eventEntity.Theme.SearchInputLabel;
            eventEntity.Theme.SearchPlaceholder = request.SearchPlaceholder?.Trim() ?? eventEntity.Theme.SearchPlaceholder;
            eventEntity.Theme.HeroImageUrl = BlankToNull(request.HeroImageUrl);
            eventEntity.Theme.UpdatedAt = DateTimeOffset.UtcNow;

            _db.SaveChanges();
            return (GetEvent(id), null);
        }

        public bool DeleteEvent(Guid id)
        {
            var eventEntity = _db.Events.SingleOrDefault(item => item.Id == id);
            if (eventEntity is null) return false;

            _db.Events.Remove(eventEntity);
            _db.SaveChanges();
            return true;
        }

        public EventDetails? SetEventStatus(Guid id, EventStatus status)
        {
            var eventEntity = _db.Events.SingleOrDefault(item => item.Id == id);
            if (eventEntity is null) return null;

            eventEntity.Status = status;
            eventEntity.IsPublic = status == EventStatus.Published;
            eventEntity.PublishedAt = status == EventStatus.Published ? DateTimeOffset.UtcNow : null;
            eventEntity.UpdatedAt = DateTimeOffset.UtcNow;
            _db.SaveChanges();

            return GetEvent(id);
        }

        public IReadOnlyList<AdminGuestDto> GetAdminGuests(Guid eventId)
        {
            var guests = _db.Guests
                .AsNoTracking()
                .Include(item => item.Table)
                .Where(item => item.EventId == eventId)
                .OrderBy(item => item.DisplayName)
                .ToArray();

            var duplicateKeys = guests
                .Select(DuplicateKey)
                .Where(key => key.Length > 0)
                .GroupBy(key => key)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);

            return guests
                .Select(item => ToAdminGuestDto(item, duplicateKeys.Contains(DuplicateKey(item))))
                .ToArray();
        }

        public (AdminGuestDto? Guest, string? Error) CreateGuest(Guid eventId, AdminGuestCreateRequest request)
        {
            if (!_db.Events.Any(item => item.Id == eventId)) return (null, "not-found");

            var guest = BuildGuest(eventId, request.FirstName, request.LastName, request.DisplayName, request.Notes);
            if (string.IsNullOrWhiteSpace(guest.DisplayName)) return (null, "Display name is required.");

            _db.Guests.Add(guest);
            _db.SaveChanges();
            return (ToAdminGuestDto(guest), null);
        }

        public (AdminGuestDto? Guest, string? Error) UpdateGuest(Guid eventId, Guid guestId, AdminGuestUpsertRequest request)
        {
            var guest = _db.Guests.Include(item => item.Table).SingleOrDefault(item => item.Id == guestId && item.EventId == eventId);
            if (guest is null) return (null, "not-found");

            var firstName = request.FirstName?.Trim() ?? string.Empty;
            var lastName = request.LastName?.Trim() ?? string.Empty;
            var displayName = BuildDisplayName(firstName, lastName, request.DisplayName);
            if (string.IsNullOrWhiteSpace(displayName)) return (null, "Display name is required.");

            EventTableEntity? table = null;
            if (request.TableId is not null)
            {
                table = _db.EventTables.Include(item => item.Guests).SingleOrDefault(item => item.Id == request.TableId && item.EventId == eventId);
                if (table is null) return (null, "not-found");

                var assignedCount = table.Guests.Count(item => item.Id != guestId && CountsTowardSeating(item.Status));
                if (assignedCount >= table.Capacity)
                {
                    return (null, $"Table {table.Code} is full.");
                }
            }

            guest.FirstName = firstName;
            guest.LastName = lastName;
            guest.DisplayName = displayName;
            guest.NormalizedSearchName = SearchNormalizer.Normalize(displayName);
            guest.Notes = request.Notes?.Trim();
            guest.TableId = request.Status == GuestStatus.Archived ? null : request.TableId;
            guest.Status = request.Status;
            guest.UpdatedAt = DateTimeOffset.UtcNow;
            _db.SaveChanges();

            guest.Table = guest.TableId is null ? null : table;
            return (ToAdminGuestDto(guest), null);
        }

        public (AdminGuestDto? Guest, string? Error) ArchiveGuest(Guid eventId, Guid guestId)
        {
            var guest = _db.Guests.Include(item => item.Table).SingleOrDefault(item => item.Id == guestId && item.EventId == eventId);
            if (guest is null) return (null, "not-found");

            guest.Status = GuestStatus.Archived;
            guest.TableId = null;
            guest.UpdatedAt = DateTimeOffset.UtcNow;
            _db.SaveChanges();
            guest.Table = null;
            return (ToAdminGuestDto(guest), null);
        }

        public bool DeleteGuest(Guid eventId, Guid guestId)
        {
            var guest = _db.Guests.SingleOrDefault(item => item.Id == guestId && item.EventId == eventId);
            if (guest is null) return false;

            _db.Guests.Remove(guest);
            _db.SaveChanges();
            return true;
        }

        public (AdminGuestDto? Guest, string? Error) AssignGuestToTable(Guid eventId, Guid guestId, Guid? tableId)
        {
            var guest = _db.Guests.Include(item => item.Table).SingleOrDefault(item => item.Id == guestId && item.EventId == eventId);
            if (guest is null) return (null, "not-found");
            if (guest.Status == GuestStatus.Archived) return (null, "Archived guests cannot be assigned to tables.");

            EventTableEntity? table = null;
            if (tableId is not null)
            {
                table = _db.EventTables.Include(item => item.Guests).SingleOrDefault(item => item.Id == tableId && item.EventId == eventId);
                if (table is null) return (null, "not-found");

                var assignedCount = table.Guests.Count(item => item.Id != guestId && CountsTowardSeating(item.Status));
                if (assignedCount >= table.Capacity)
                {
                    return (null, $"Table {table.Code} is full.");
                }
            }

            guest.TableId = tableId;
            guest.UpdatedAt = DateTimeOffset.UtcNow;
            _db.SaveChanges();

            guest.Table = table;
            return (ToAdminGuestDto(guest), null);
        }

        public (AdminGuestImportPreviewDto? Preview, string? Error) PreviewGuestImport(Guid eventId, IReadOnlyList<AdminGuestImportRow> rows)
        {
            if (!_db.Events.Any(item => item.Id == eventId)) return (null, "not-found");

            var existingKeys = _db.Guests
                .AsNoTracking()
                .Where(item => item.EventId == eventId)
                .Select(item => item.NormalizedSearchName)
                .Where(item => item != string.Empty)
                .ToHashSet(StringComparer.Ordinal);

            var seenKeys = new Dictionary<string, int>(StringComparer.Ordinal);
            var previewRows = rows
                .Select((row, index) => BuildImportPreviewRow(row, index + 2, existingKeys, seenKeys))
                .ToArray();

            return (new AdminGuestImportPreviewDto(previewRows, previewRows.Count(item => item.Errors.Length > 0), previewRows.Count(item => item.IsDuplicate)), null);
        }

        public (IReadOnlyList<AdminGuestDto>? Guests, string? Error) ImportGuests(Guid eventId, IReadOnlyList<AdminGuestImportRow> rows)
        {
            var preview = PreviewGuestImport(eventId, rows);
            if (preview.Error is not null) return (null, preview.Error);

            var errors = preview.Preview!.Rows.Where(item => item.Errors.Length > 0).ToArray();
            if (errors.Length > 0)
            {
                return (null, "Resolve import errors before saving guests.");
            }

            var guests = preview.Preview.Rows
                .Where(item => !item.IsDuplicate)
                .Select(item => BuildGuest(eventId, item.FirstName, item.LastName, item.DisplayName, item.Notes))
                .ToArray();

            _db.Guests.AddRange(guests);
            _db.SaveChanges();
            return (guests.Select(item => ToAdminGuestDto(item)).ToArray(), null);
        }

        public string ExportGuestsCsv(Guid eventId)
        {
            static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

            var guests = GetAdminGuests(eventId);
            var builder = new StringBuilder();
            builder.AppendLine("First Name,Last Name,Display Name,Notes,Status,Table Number,Table Name");
            foreach (var guest in guests)
            {
                builder.AppendLine(string.Join(',', [
                    Csv(guest.FirstName),
                    Csv(guest.LastName),
                    Csv(guest.DisplayName),
                    Csv(guest.Notes),
                    Csv(guest.Status.ToString()),
                    Csv(guest.TableCode),
                    Csv(guest.TableName)
                ]));
            }

            return builder.ToString();
        }

        public IReadOnlyList<AdminTableDto> GetAdminTables(Guid eventId)
        {
            return _db.EventTables
                .AsNoTracking()
                .Include(item => item.Guests)
                .Where(item => item.EventId == eventId)
                .OrderBy(item => item.Code)
                .Select(item => ToAdminTableDto(item))
                .ToArray();
        }

        public (AdminTableDto? Table, string? Error) CreateTable(Guid eventId, AdminTableCreateRequest request)
        {
            if (!_db.Events.Any(item => item.Id == eventId)) return (null, "not-found");

            var number = request.Number.Trim();
            if (_db.EventTables.Any(item => item.EventId == eventId && item.Code == number))
            {
                return (null, "A table with this number already exists.");
            }

            var capacity = Math.Max(1, request.MaximumCapacity);
            var table = new EventTableEntity
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                Name = request.Name.Trim(),
                Code = number,
                Capacity = capacity,
                Shape = "Round",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _db.EventTables.Add(table);

            var floorPlan = GetOrCreateActiveFloorPlan(eventId);
            floorPlan.Objects.Add(new FloorPlanObjectEntity
            {
                Id = $"table-{eventId:N}-{Slugify(number)}",
                FloorPlanId = floorPlan.Id,
                LinkedTableId = table.Id,
                ObjectType = "table",
                Label = $"Table {number}",
                X = 0.12m + (_db.EventTables.Count(item => item.EventId == eventId) % 4) * 0.18m,
                Y = 0.24m,
                Width = 0.14m,
                Height = 0.14m,
                Shape = "round",
                ZIndex = 10,
                IsVisible = true
            });

            _db.SaveChanges();
            return (ToAdminTableDto(table), null);
        }

        public FloorPlanDto? SaveFloorPlan(Guid eventId, FloorPlanSaveRequest request)
        {
            if (!_db.Events.Any(item => item.Id == eventId)) return null;

            var floorPlan = GetOrCreateActiveFloorPlan(eventId);
            var existingObjects = _db.FloorPlanObjects.Where(item => item.FloorPlanId == floorPlan.Id).ToList();
            _db.FloorPlanObjects.RemoveRange(existingObjects);

            foreach (var item in request.Objects)
            {
                _db.FloorPlanObjects.Add(new FloorPlanObjectEntity
                {
                    Id = string.IsNullOrWhiteSpace(item.Id) ? $"{item.ObjectType}-{Guid.NewGuid():N}" : item.Id,
                    FloorPlanId = floorPlan.Id,
                    LinkedTableId = item.LinkedTableId,
                    ObjectType = item.ObjectType.Trim(),
                    Label = item.Label.Trim(),
                    X = Clamp01(item.X),
                    Y = Clamp01(item.Y),
                    Width = Math.Clamp(item.Width, 0.04m, 1m),
                    Height = Math.Clamp(item.Height, 0.04m, 1m),
                    Shape = item.Shape,
                    ZIndex = item.ZIndex,
                    IsVisible = true
                });
            }

            floorPlan.Version += 1;
            _db.SaveChanges();
            return GetEvent(eventId)?.FloorPlan;
        }

        public void SaveMessage(string slug, string publicToken, string message)
        {
            var guest = _db.Guests
                .Include(item => item.Event)
                .SingleOrDefault(item => item.Event != null && item.Event.Slug.ToLower() == slug.ToLower() && item.PublicToken == publicToken);
            if (guest is null) return;

            _db.GuestMessages.Add(new GuestMessageEntity
            {
                Id = Guid.NewGuid(),
                EventId = guest.EventId,
                GuestId = guest.Id,
                Message = message,
                CreatedAt = DateTimeOffset.UtcNow
            });
            _db.SaveChanges();
        }

        public void TrackSearch(string slug, string normalizedQuery, bool successful)
        {
            var eventId = _db.Events
                .Where(item => item.Slug.ToLower() == slug.ToLower())
                .Select(item => (Guid?)item.Id)
                .SingleOrDefault();
            if (eventId is null) return;

            _db.SearchMetrics.Add(new SearchMetricEntity
            {
                Id = Guid.NewGuid(),
                EventId = eventId.Value,
                NormalizedQuery = normalizedQuery,
                Successful = successful,
                CreatedAt = DateTimeOffset.UtcNow
            });
            _db.SaveChanges();
        }

        private IQueryable<EventEntity> EventQuery()
        {
            return _db.Events
                .AsNoTracking()
                .Include(item => item.Theme)
                .Include(item => item.Tables)
                .Include(item => item.Guests)
                    .ThenInclude(item => item.Table)
                .Include(item => item.Guests)
                    .ThenInclude(item => item.SearchAliases)
                .Include(item => item.FloorPlans)
                    .ThenInclude(item => item.Objects);
        }

        private void EnsureEventContentSchema()
        {
            _db.Database.ExecuteSqlRaw("""
                alter table events
                  add column if not exists event_type text not null default 'Wedding';

                alter table event_themes
                  alter column primary_color set default '#D8CFBC',
                  alter column background_color set default '#FFFBF4',
                  alter column text_color set default '#11120D',
                  add column if not exists secondary_color text not null default '#565449',
                  add column if not exists welcome_title text not null default '',
                  add column if not exists search_input_label text not null default 'Search by name',
                  add column if not exists search_placeholder text not null default 'Search by name';

                alter table guests
                  add column if not exists first_name text not null default '',
                  add column if not exists last_name text not null default '',
                  add column if not exists notes text,
                  add column if not exists status text not null default 'Active';

                create index if not exists ix_guests_event_status_table on guests(event_id, status, table_id);
            """);
        }

        private OrganizationEntity GetOrCreateDemoOrganization()
        {
            var organization = _db.Organizations.SingleOrDefault(item => item.Slug == "demo-events");
            if (organization is not null) return organization;

            organization = new OrganizationEntity
            {
                Id = Guid.NewGuid(),
                Name = "Demo Events",
                Slug = "demo-events",
                Status = "Active",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _db.Organizations.Add(organization);
            _db.SaveChanges();
            return organization;
        }

        private static EventDetails ToEventDetails(EventEntity eventEntity)
        {
            var theme = eventEntity.Theme is null
                ? new EventTheme(BuildLogoText(eventEntity.Name), string.Empty, "#D8CFBC", "#565449", "#FFFBF4", "#11120D", $"Welcome to {eventEntity.Name}", "Search by name", "Search by name", null)
                : new EventTheme(
                    eventEntity.Theme.LogoText,
                    eventEntity.Theme.HeroText,
                    eventEntity.Theme.PrimaryColor,
                    eventEntity.Theme.SecondaryColor,
                    eventEntity.Theme.BackgroundColor,
                    eventEntity.Theme.TextColor,
                    string.IsNullOrWhiteSpace(eventEntity.Theme.WelcomeTitle) ? $"Welcome to {eventEntity.Name}" : eventEntity.Theme.WelcomeTitle,
                    string.IsNullOrWhiteSpace(eventEntity.Theme.SearchInputLabel) ? "Search by name" : eventEntity.Theme.SearchInputLabel,
                    string.IsNullOrWhiteSpace(eventEntity.Theme.SearchPlaceholder) ? "Search by name" : eventEntity.Theme.SearchPlaceholder,
                    eventEntity.Theme.HeroImageUrl);

            var floorPlan = eventEntity.FloorPlans
                .Where(item => item.IsActive)
                .OrderByDescending(item => item.Version)
                .FirstOrDefault();

            return new EventDetails(
                eventEntity.Id,
                eventEntity.Name,
                eventEntity.Slug,
                eventEntity.EventType,
                eventEntity.Subtitle,
                eventEntity.DateLabel,
                eventEntity.VenueName,
                eventEntity.VenueAddress,
                eventEntity.Status,
                theme,
                new FloorPlanDto(
                    floorPlan?.Name ?? "Venue layout",
                    floorPlan?.CanvasAspectRatio ?? 1.14m,
                    floorPlan?.Objects
                        .Where(item => item.IsVisible)
                        .OrderBy(item => item.ZIndex)
                        .Select(item => new FloorPlanObjectDto(
                            item.Id,
                            item.ObjectType,
                            item.Label,
                            item.LinkedTableId,
                            item.LinkedTableId is null ? null : eventEntity.Tables.FirstOrDefault(table => table.Id == item.LinkedTableId)?.Code,
                            item.X,
                            item.Y,
                            item.Width,
                            item.Height,
                            item.Shape,
                            item.ZIndex))
                        .ToArray() ?? []),
                eventEntity.Guests.Select(ToGuest).ToList());
        }

        private static Guest ToGuest(GuestEntity guest)
        {
            var aliases = guest.SearchAliases.Select(item => item.Alias).ToArray();
            var tableCode = guest.Table?.Code ?? string.Empty;
            var tableName = guest.Table?.Name ?? string.Empty;

            return new Guest(
                guest.Id,
                guest.PublicToken,
                guest.DisplayName,
                guest.GroupLabel,
                tableCode,
                tableName,
                guest.SeatNumber,
                guest.Directions,
                guest.Status,
                aliases,
                []);
        }

        private FloorPlanEntity GetOrCreateActiveFloorPlan(Guid eventId)
        {
            var floorPlan = _db.FloorPlans
                .Include(item => item.Objects)
                .SingleOrDefault(item => item.EventId == eventId && item.IsActive);
            if (floorPlan is not null) return floorPlan;

            floorPlan = new FloorPlanEntity
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                Name = "Venue layout",
                CanvasAspectRatio = 1.14m,
                Version = 1,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.FloorPlans.Add(floorPlan);
            _db.SaveChanges();
            return floorPlan;
        }

        private GuestEntity BuildGuest(Guid eventId, string? firstNameValue, string? lastNameValue, string? displayNameValue, string? notesValue)
        {
            var firstName = firstNameValue?.Trim() ?? string.Empty;
            var lastName = lastNameValue?.Trim() ?? string.Empty;
            var displayName = BuildDisplayName(firstName, lastName, displayNameValue);

            return new GuestEntity
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                FirstName = firstName,
                LastName = lastName,
                DisplayName = displayName,
                NormalizedSearchName = SearchNormalizer.Normalize(displayName),
                PublicToken = BuildGuestToken(displayName),
                Notes = notesValue?.Trim(),
                Status = GuestStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        private string BuildGuestToken(string displayName)
        {
            var tokenBase = Slugify(displayName);
            var token = $"guest-{tokenBase}";
            var suffix = 2;
            while (_db.Guests.Any(item => item.PublicToken == token))
            {
                token = $"guest-{tokenBase}-{suffix++}";
            }

            return token;
        }

        private static string BuildDisplayName(string firstName, string lastName, string? displayName)
        {
            return string.IsNullOrWhiteSpace(displayName)
                ? $"{firstName} {lastName}".Trim()
                : displayName.Trim();
        }

        private static AdminGuestImportRowDto BuildImportPreviewRow(AdminGuestImportRow row, int fallbackRowNumber, HashSet<string> existingKeys, Dictionary<string, int> seenKeys)
        {
            var firstName = row.FirstName?.Trim() ?? string.Empty;
            var lastName = row.LastName?.Trim() ?? string.Empty;
            var displayName = BuildDisplayName(firstName, lastName, row.DisplayName);
            var notes = row.Notes?.Trim() ?? string.Empty;
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(displayName))
            {
                errors.Add("First name or display name is required.");
            }

            var duplicateKey = SearchNormalizer.Normalize(displayName);
            var duplicateInFile = false;
            if (!string.IsNullOrWhiteSpace(duplicateKey))
            {
                seenKeys.TryGetValue(duplicateKey, out var count);
                duplicateInFile = count > 0;
                seenKeys[duplicateKey] = count + 1;
            }

            var isDuplicate = duplicateInFile || existingKeys.Contains(duplicateKey);
            if (isDuplicate)
            {
                errors.Add("Possible duplicate guest.");
            }

            return new AdminGuestImportRowDto(row.RowNumber ?? fallbackRowNumber, firstName, lastName, displayName, notes, isDuplicate, errors.ToArray());
        }

        private static string DuplicateKey(GuestEntity guest)
        {
            return SearchNormalizer.Normalize(guest.DisplayName);
        }

        private static AdminGuestDto ToAdminGuestDto(GuestEntity guest, bool isDuplicate = false)
        {
            return new AdminGuestDto(
                guest.Id,
                guest.FirstName,
                guest.LastName,
                guest.DisplayName,
                guest.Notes ?? string.Empty,
                guest.TableId,
                guest.Table?.Code ?? string.Empty,
                guest.Table?.Name ?? string.Empty,
                guest.Status,
                isDuplicate);
        }

        private static AdminTableDto ToAdminTableDto(EventTableEntity table)
        {
            return new AdminTableDto(
                table.Id,
                table.Name,
                table.Code,
                table.Capacity,
                table.Guests.Count(guest => CountsTowardSeating(guest.Status)));
        }

        private static bool CountsTowardSeating(GuestStatus status)
        {
            return status is GuestStatus.Active or GuestStatus.CheckedIn;
        }

        private static string BuildLogoText(string name)
        {
            var parts = name.Split([' ', '&', '+', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Join(" ", parts.Take(2).Select(part => part[0])).ToUpperInvariant();
        }

        private static string NormalizeEventType(string? eventType)
        {
            return string.IsNullOrWhiteSpace(eventType) ? "Wedding" : eventType.Trim();
        }

        private static string? BlankToNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string Slugify(string value)
        {
            var slug = Regex.Replace(value.ToLowerInvariant().Trim(), "[^a-z0-9]+", "-").Trim('-');
            return string.IsNullOrWhiteSpace(slug) ? Guid.NewGuid().ToString("N")[..8] : slug;
        }

        private static decimal Clamp01(decimal value)
        {
            return Math.Clamp(value, 0m, 1m);
        }
    }

    public sealed class AuthOptions
    {
        public string Issuer { get; set; } = "sassoir.local";
        public string Audience { get; set; } = "sassoir.admin";
        public string SigningKey { get; set; } = string.Empty;
        public int AccessTokenMinutes { get; set; } = 30;
        public string SeedAdminEmail { get; set; } = "admin@sassoir.com";
        public string SeedAdminPassword { get; set; } = string.Empty;
    }

    public sealed class AuthStore
    {
        private readonly SassoirDbContext _db;
        private readonly AuthOptions _options;

        public AuthStore(SassoirDbContext db, IOptions<AuthOptions> options)
        {
            _db = db;
            _options = options.Value;
            EnsureAuthTables();
            EnsureSeedAdmin();
        }

        public LoginResponse? Login(string email, string password)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var user = _db.Users
                .Include(item => item.UserRoles)
                    .ThenInclude(item => item.Role)
                .SingleOrDefault(item => item.Email.ToLower() == normalizedEmail && item.Status == "Active");

            if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
            {
                return null;
            }

            user.LastLoginAt = DateTimeOffset.UtcNow;
            _db.SaveChanges();

            var roles = user.UserRoles.Select(item => item.Role?.Name).Where(item => item is not null).Cast<string>().ToArray();
            return new LoginResponse(
                TokenSigner.Create(user, roles, _options),
                user.Email,
                $"{user.FirstName} {user.LastName}".Trim(),
                roles,
                DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes));
        }

        public CurrentUserDto? GetCurrentUser(HttpRequest request)
        {
            var claims = ValidateRequest(request);
            return claims is null
                ? null
                : new CurrentUserDto(claims.Email, claims.DisplayName, claims.Roles);
        }

        public bool IsAdmin(HttpRequest request)
        {
            var claims = ValidateRequest(request);
            return claims?.Roles.Any(role => role is "Admin" or "SuperAdmin") == true;
        }

        private TokenClaims? ValidateRequest(HttpRequest request)
        {
            var authorization = request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;

            return TokenSigner.Validate(authorization["Bearer ".Length..].Trim(), _options);
        }

        private void EnsureAuthTables()
        {
            _db.Database.ExecuteSqlRaw("""
                create table if not exists app_users (
                  id uuid primary key,
                  organization_id uuid null references organizations(id) on delete set null,
                  first_name text not null,
                  last_name text not null,
                  email text not null unique,
                  password_hash text not null,
                  status text not null default 'Active',
                  is_super_admin boolean not null default false,
                  last_login_at timestamptz null,
                  created_at timestamptz not null default now(),
                  updated_at timestamptz not null default now()
                );

                create table if not exists roles (
                  id uuid primary key,
                  name text not null unique
                );

                create table if not exists user_roles (
                  user_id uuid not null references app_users(id) on delete cascade,
                  role_id uuid not null references roles(id) on delete cascade,
                  primary key (user_id, role_id)
                );
            """);
        }

        private void EnsureSeedAdmin()
        {
            if (string.IsNullOrWhiteSpace(_options.SeedAdminEmail) || string.IsNullOrWhiteSpace(_options.SeedAdminPassword))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var adminRole = _db.Roles.SingleOrDefault(item => item.Name == "Admin");
            if (adminRole is null)
            {
                adminRole = new RoleEntity { Id = Guid.NewGuid(), Name = "Admin" };
                _db.Roles.Add(adminRole);
                _db.SaveChanges();
            }

            var normalizedEmail = _options.SeedAdminEmail.Trim().ToLowerInvariant();
            var user = _db.Users
                .Include(item => item.UserRoles)
                .SingleOrDefault(item => item.Email.ToLower() == normalizedEmail);

            if (user is null)
            {
                user = new AppUserEntity
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Sassoir",
                    LastName = "Admin",
                    Email = normalizedEmail,
                    PasswordHash = PasswordHasher.Hash(_options.SeedAdminPassword),
                    Status = "Active",
                    IsSuperAdmin = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.Users.Add(user);
                _db.SaveChanges();
            }

            if (!_db.UserRoles.Any(item => item.UserId == user.Id && item.RoleId == adminRole.Id))
            {
                _db.UserRoles.Add(new UserRoleEntity { UserId = user.Id, RoleId = adminRole.Id });
                _db.SaveChanges();
            }
        }
    }

    public sealed record TokenClaims(Guid UserId, string Email, string DisplayName, string[] Roles);

    public static class TokenSigner
    {
        public static string Create(AppUserEntity user, string[] roles, AuthOptions options)
        {
            var now = DateTimeOffset.UtcNow;
            var payload = new Dictionary<string, object?>
            {
                ["iss"] = options.Issuer,
                ["aud"] = options.Audience,
                ["sub"] = user.Id.ToString(),
                ["email"] = user.Email,
                ["name"] = $"{user.FirstName} {user.LastName}".Trim(),
                ["roles"] = roles,
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = now.AddMinutes(options.AccessTokenMinutes).ToUnixTimeSeconds()
            };

            var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
            var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
            var unsigned = $"{header}.{body}";
            var signature = Sign(unsigned, options.SigningKey);
            return $"{unsigned}.{signature}";
        }

        public static TokenClaims? Validate(string token, AuthOptions options)
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return null;

            var unsigned = $"{parts[0]}.{parts[1]}";
            var expected = Sign(unsigned, options.SigningKey);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2])))
            {
                return null;
            }

            using var document = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var root = document.RootElement;
            if (root.GetProperty("iss").GetString() != options.Issuer) return null;
            if (root.GetProperty("aud").GetString() != options.Audience) return null;
            if (root.GetProperty("exp").GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            if (!Guid.TryParse(root.GetProperty("sub").GetString(), out var userId)) return null;

            var roles = root.GetProperty("roles").EnumerateArray().Select(item => item.GetString()).Where(item => item is not null).Cast<string>().ToArray();
            return new TokenClaims(
                userId,
                root.GetProperty("email").GetString() ?? string.Empty,
                root.GetProperty("name").GetString() ?? string.Empty,
                roles);
        }

        private static string Sign(string value, string signingKey)
        {
            if (signingKey.Length < 32)
            {
                throw new InvalidOperationException("Auth signing key must be at least 32 characters.");
            }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
            return Base64Url(hmac.ComputeHash(Encoding.ASCII.GetBytes(value)));
        }

        private static string Base64Url(byte[] value)
        {
            return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return Convert.FromBase64String(padded);
        }
    }

    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int KeySize = 32;
        private const int Iterations = 100_000;

        public static string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            return $"pbkdf2-sha256.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
        }

        public static bool Verify(string password, string storedHash)
        {
            var parts = storedHash.Split('.');
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
            if (!int.TryParse(parts[1], out var iterations)) return false;

            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }

    public static partial class AdminEventValidator
    {
        public static Dictionary<string, string[]> Validate(AdminEventUpsertRequest request)
        {
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                errors["name"] = ["Event name is required."];
            }

            if (string.IsNullOrWhiteSpace(request.Slug))
            {
                errors["slug"] = ["Slug is required."];
            }
            else if (!SlugRegex().IsMatch(request.Slug))
            {
                errors["slug"] = ["Use lowercase letters, numbers, and hyphens only."];
            }

            if (!ColorIsValid(request.PrimaryColor)) errors["primaryColor"] = ["Use a valid hex color."];
            if (!ColorIsValid(request.SecondaryColor)) errors["secondaryColor"] = ["Use a valid hex color."];
            if (!ColorIsValid(request.BackgroundColor)) errors["backgroundColor"] = ["Use a valid hex color."];
            if (!ColorIsValid(request.TextColor)) errors["textColor"] = ["Use a valid hex color."];

            return errors;
        }

        private static bool ColorIsValid(string? value)
        {
            return string.IsNullOrWhiteSpace(value) || HexColorRegex().IsMatch(value);
        }

        [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
        private static partial Regex SlugRegex();

        [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
        private static partial Regex HexColorRegex();
    }

    public static class SearchNormalizer
    {
        public static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var withoutAccents = new string(value
                .Normalize(NormalizationForm.FormD)
                .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                .ToArray());

            return withoutAccents
                .Replace("\u0623", "\u0627")
                .Replace("\u0625", "\u0627")
                .Replace("\u0622", "\u0627")
                .Replace("\u0671", "\u0627")
                .Replace("\u0649", "\u064a")
                .Replace("\u0624", "\u0648")
                .Replace("\u0626", "\u064a")
                .Replace("\u0629", "\u0647")
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Aggregate(string.Empty, (current, part) => current.Length == 0 ? part : $"{current} {part}");
        }

        public static int Rank(Guest guest, string normalizedQuery)
        {
            var displayName = Normalize(guest.DisplayName);
            var aliases = guest.SearchAliases.Select(Normalize).ToArray();

            if (displayName == normalizedQuery) return 1;
            if (aliases.Contains(normalizedQuery)) return 2;
            if (displayName.StartsWith(normalizedQuery, StringComparison.Ordinal)) return 3;
            if (aliases.Any(alias => alias.StartsWith(normalizedQuery, StringComparison.Ordinal))) return 4;
            if (displayName.Contains(normalizedQuery, StringComparison.Ordinal)) return 5;
            if (aliases.Any(alias => alias.Contains(normalizedQuery, StringComparison.Ordinal))) return 6;

            return 99;
        }
    }
}

namespace Sassoir.Api.Models
{
    public enum EventStatus
    {
        Draft,
        Published,
        Archived
    }

    public enum GuestStatus
    {
        Active,
        Cancelled,
        CheckedIn,
        Archived
    }

    public sealed record EventDetails(
        Guid Id,
        string Name,
        string Slug,
        string EventType,
        string Subtitle,
        string DateLabel,
        string VenueName,
        string VenueAddress,
        EventStatus Status,
        EventTheme Theme,
        FloorPlanDto FloorPlan,
        List<Guest> Guests);

    public sealed record EventTheme(
        string LogoText,
        string HeroText,
        string PrimaryColor,
        string SecondaryColor,
        string BackgroundColor,
        string TextColor,
        string WelcomeTitle,
        string SearchInputLabel,
        string SearchPlaceholder,
        string? HeroImageUrl);

    public sealed record Guest(
        Guid Id,
        string PublicToken,
        string DisplayName,
        string GroupLabel,
        string TableCode,
        string TableName,
        string? SeatNumber,
        string Directions,
        GuestStatus Status,
        string[] SearchAliases,
        string[] Companions);

    public sealed record FloorPlanDto(string Name, decimal CanvasAspectRatio, FloorPlanObjectDto[] Objects);

    public sealed record FloorPlanObjectDto(
        string Id,
        string ObjectType,
        string Label,
        Guid? LinkedTableId,
        string? TableCode,
        decimal X,
        decimal Y,
        decimal Width,
        decimal Height,
        string Shape,
        int ZIndex);

    public sealed record LoginRequest(string Email, string Password);

    public sealed record LoginResponse(string Token, string Email, string DisplayName, string[] Roles, DateTimeOffset ExpiresAt);

    public sealed record CurrentUserDto(string Email, string DisplayName, string[] Roles);

    public sealed record UploadResponse(string Url);

    public sealed record AdminGuestDto(
        Guid Id,
        string FirstName,
        string LastName,
        string DisplayName,
        string Notes,
        Guid? TableId,
        string TableCode,
        string TableName,
        GuestStatus Status,
        bool IsDuplicate);

    public sealed record AdminGuestCreateRequest(string? FirstName, string? LastName, string? DisplayName, string? Notes);

    public sealed record AdminGuestUpsertRequest(string? FirstName, string? LastName, string? DisplayName, string? Notes, Guid? TableId, GuestStatus Status);

    public sealed record AdminGuestImportRequest(IReadOnlyList<AdminGuestImportRow> Guests);

    public sealed record AdminGuestImportRow(int? RowNumber, string? FirstName, string? LastName, string? DisplayName, string? Notes);

    public sealed record AdminGuestImportPreviewDto(AdminGuestImportRowDto[] Rows, int ErrorCount, int DuplicateCount);

    public sealed record AdminGuestImportRowDto(int RowNumber, string FirstName, string LastName, string DisplayName, string Notes, bool IsDuplicate, string[] Errors);

    public sealed record AssignGuestTableRequest(Guid? TableId);

    public sealed record AdminTableDto(Guid Id, string Name, string Number, int MaximumCapacity, int AssignedGuestCount);

    public sealed record AdminTableCreateRequest(string Name, string Number, int MaximumCapacity);

    public sealed record FloorPlanSaveRequest(FloorPlanObjectSaveDto[] Objects);

    public sealed record FloorPlanObjectSaveDto(
        string Id,
        string ObjectType,
        string Label,
        Guid? LinkedTableId,
        decimal X,
        decimal Y,
        decimal Width,
        decimal Height,
        string Shape,
        int ZIndex);

    public sealed record GuestSearchRequest(string Query);

    public sealed record GuestSearchResponse(IReadOnlyCollection<GuestSearchResultDto> Results);

    public sealed record GuestSearchResultDto(string PublicToken, string DisplayName, string GroupLabel);

    public sealed record AdminEventUpsertRequest(
        string Name,
        string Slug,
        string? Subtitle,
        string? DateLabel,
        string? VenueName,
        string? VenueAddress,
        string? EventType,
        EventStatus Status,
        string? HeroText,
        string? PrimaryColor,
        string? SecondaryColor,
        string? BackgroundColor,
        string? TextColor,
        string? WelcomeTitle,
        string? SearchInputLabel,
        string? SearchPlaceholder,
        string? HeroImageUrl);

    public sealed record AdminEventDto(
        Guid Id,
        string Name,
        string Slug,
        string EventType,
        string Subtitle,
        string DateLabel,
        string VenueName,
        string VenueAddress,
        EventStatus Status,
        string HeroText,
        string PrimaryColor,
        string SecondaryColor,
        string BackgroundColor,
        string TextColor,
        string WelcomeTitle,
        string SearchInputLabel,
        string SearchPlaceholder,
        string? HeroImageUrl,
        int GuestCount,
        int AssignedGuests);

    public sealed record PublicEventDto(
        string Name,
        string Slug,
        string EventType,
        string Subtitle,
        string DateLabel,
        string VenueName,
        string VenueAddress,
        EventTheme Theme);

    public sealed record SeatResultDto(
        string DisplayName,
        string GroupLabel,
        string TableCode,
        string TableName,
        string? SeatNumber,
        string Directions,
        string[] Companions,
        PublicEventDto Event);

    public sealed record GuestFloorPlanDto(FloorPlanDto FloorPlan, string HighlightedObjectId);

    public sealed record GuestMessageRequest(string Message);

    public sealed record GuestMessage(string EventSlug, string PublicToken, string Message, DateTimeOffset CreatedAt);

    public sealed record SearchMetric(string EventSlug, string NormalizedQuery, bool Successful, DateTimeOffset CreatedAt);

    public static class DtoMapping
    {
        public static PublicEventDto ToPublicDto(this EventDetails eventDetails)
        {
            return new PublicEventDto(
                eventDetails.Name,
                eventDetails.Slug,
                eventDetails.EventType,
                eventDetails.Subtitle,
                eventDetails.DateLabel,
                eventDetails.VenueName,
                eventDetails.VenueAddress,
                eventDetails.Theme);
        }

        public static AdminEventDto ToAdminDto(this EventDetails eventDetails)
        {
            return new AdminEventDto(
                eventDetails.Id,
                eventDetails.Name,
                eventDetails.Slug,
                eventDetails.EventType,
                eventDetails.Subtitle,
                eventDetails.DateLabel,
                eventDetails.VenueName,
                eventDetails.VenueAddress,
                eventDetails.Status,
                eventDetails.Theme.HeroText,
                eventDetails.Theme.PrimaryColor,
                eventDetails.Theme.SecondaryColor,
                eventDetails.Theme.BackgroundColor,
                eventDetails.Theme.TextColor,
                eventDetails.Theme.WelcomeTitle,
                eventDetails.Theme.SearchInputLabel,
                eventDetails.Theme.SearchPlaceholder,
                eventDetails.Theme.HeroImageUrl,
                eventDetails.Guests.Count(guest => guest.Status != GuestStatus.Archived),
                eventDetails.Guests.Count(guest => guest.Status != GuestStatus.Archived && !string.IsNullOrWhiteSpace(guest.TableCode)));
        }

        public static GuestSearchResultDto ToSearchDto(this Guest guest)
        {
            return new GuestSearchResultDto(guest.PublicToken, guest.DisplayName, guest.GroupLabel);
        }

        public static SeatResultDto ToSeatDto(this Guest guest, EventDetails eventDetails)
        {
            var companions = string.IsNullOrWhiteSpace(guest.TableCode)
                ? []
                : eventDetails.Guests
                    .Where(item => item.Status == GuestStatus.Active && item.PublicToken != guest.PublicToken && item.TableCode == guest.TableCode)
                    .Select(item => item.DisplayName)
                    .ToArray();

            return new SeatResultDto(
                guest.DisplayName,
                guest.GroupLabel,
                guest.TableCode,
                guest.TableName,
                guest.SeatNumber,
                guest.Directions,
                companions,
                eventDetails.ToPublicDto());
        }
    }
}
