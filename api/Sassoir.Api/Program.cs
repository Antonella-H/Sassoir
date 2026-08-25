using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Npgsql;
using Sassoir.Api.Data;
using Sassoir.Api.Models;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var allowedOrigins = GetAllowedOrigins(builder.Configuration);

builder.Services.AddDbContext<SassoirDbContext>(options =>
{
    options.UseNpgsql(NormalizePostgresConnectionString(builder.Configuration.GetConnectionString("DefaultConnection"), builder.Configuration));
});
builder.Services.AddScoped<EventStore>();
builder.Services.AddMemoryCache();
builder.Services.AddHealthChecks();
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

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Fastest);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("PublicEvent", context => FixedWindowPolicy(context, "RateLimiting:PublicEventPerMinute", 60));
    options.AddPolicy("PublicSearch", context => FixedWindowPolicy(context, "RateLimiting:GuestSearchPerMinute", 30));
    options.AddPolicy("PublicSeat", context => FixedWindowPolicy(context, "RateLimiting:SeatResultPerMinute", 30));
    options.AddPolicy("PublicMessage", context => FixedWindowPolicy(context, "RateLimiting:GuestMessagePerMinute", 5));
});

var app = builder.Build();

app.Logger.LogInformation("Configured CORS origins: {CorsOrigins}", string.Join(", ", allowedOrigins));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SassoirDbContext>();
    DatabaseInitializer.EnsureSchema(db);
}

app.UseResponseCompression();
app.UseCors("ConfiguredWebOrigins");
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString("N");
    }

    context.Response.Headers["X-Correlation-ID"] = correlationId;
    var started = System.Diagnostics.Stopwatch.StartNew();

    try
    {
        await next();
    }
    finally
    {
        started.Stop();
        var endpoint = context.GetEndpoint()?.DisplayName ?? $"{context.Request.Method} {context.Request.Path}";
        var logLevel = started.ElapsedMilliseconds >= app.Configuration.GetValue("Performance:SlowRequestMilliseconds", 750)
            ? LogLevel.Warning
            : LogLevel.Information;
        app.Logger.Log(logLevel,
            "HTTP {Method} {Path} completed {StatusCode} in {ElapsedMilliseconds}ms correlationId={CorrelationId} endpoint={Endpoint}",
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            started.ElapsedMilliseconds,
            correlationId,
            endpoint);
    }
});

var configuredUploadRoot = app.Configuration["Uploads:RootPath"];
var uploadRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredUploadRoot)
    ? Path.Combine(app.Environment.ContentRootPath, "uploads")
    : configuredUploadRoot);
Directory.CreateDirectory(uploadRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadRoot),
    RequestPath = "/api/uploads",
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl = "public,max-age=86400";
    }
});

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

app.MapPost("/api/auth/login", (LoginRequest request, AuthStore auth) =>
{
    var login = auth.Login(request.Email, request.Password);
    return login is null ? Results.Unauthorized() : Results.Ok(login);
});

app.MapPost("/api/auth/refresh", (RefreshTokenRequest request, AuthStore auth) =>
{
    var login = auth.Refresh(request.RefreshToken);
    return login is null ? Results.Unauthorized() : Results.Ok(login);
});

app.MapGet("/api/auth/me", (HttpRequest request, AuthStore auth) =>
{
    var user = auth.GetCurrentUser(request);
    return user is null ? Results.Unauthorized() : Results.Ok(user);
});

app.MapPost("/api/auth/change-password", (ChangePasswordRequest password, HttpRequest request, AuthStore auth) =>
{
    var result = auth.ChangePassword(request, password.CurrentPassword, password.NewPassword);
    return result switch
    {
        "unauthorized" => Results.Unauthorized(),
        not null => Results.BadRequest(new { message = result }),
        _ => Results.Ok(new { status = "updated" })
    };
});

app.MapPost("/api/auth/forgot-password", (ForgotPasswordRequest password, AuthStore auth) =>
{
    var reset = auth.CreatePasswordReset(password.Email);
    return Results.Ok(new
    {
        message = "If the email belongs to an admin account, a reset link can be sent.",
        resetToken = reset
    });
});

app.MapPost("/api/auth/reset-password", (ResetPasswordRequest password, AuthStore auth) =>
{
    var result = auth.ResetPassword(password.ResetToken, password.NewPassword);
    return result switch
    {
        "unauthorized" => Results.Unauthorized(),
        not null => Results.BadRequest(new { message = result }),
        _ => Results.Ok(new { status = "updated" })
    };
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

app.Run();

static bool ContactEmailIsValid(string email)
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

static string? NormalizePostgresConnectionString(string? connectionString, IConfiguration configuration)
{
    if (string.IsNullOrWhiteSpace(connectionString)) return connectionString;
    var maxPoolSize = Math.Max(5, configuration.GetValue("Database:MaxPoolSize", 20));
    var commandTimeout = Math.Max(5, configuration.GetValue("Database:CommandTimeoutSeconds", 30));

    if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        var existingBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = true,
            MaxPoolSize = maxPoolSize,
            CommandTimeout = commandTimeout
        };
        return existingBuilder.ConnectionString;
    }

    var uri = new Uri(connectionString);
    var credentials = uri.UserInfo.Split(':', 2);
    var database = uri.AbsolutePath.TrimStart('/');

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Database = Uri.UnescapeDataString(database),
        Username = credentials.Length > 0 ? Uri.UnescapeDataString(credentials[0]) : string.Empty,
        Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : string.Empty,
        SslMode = SslMode.Require,
        Pooling = true,
        MaxPoolSize = maxPoolSize,
        CommandTimeout = commandTimeout
    };

    return builder.ConnectionString;
}

static string[] GetAllowedOrigins(IConfiguration configuration)
{
    string[] defaultOrigins =
    [
        "https://sassoir.com",
        "https://www.sassoir.com",
        "http://127.0.0.1:5173",
        "http://localhost:5173"
    ];

    var configuredOrigins = configuration
        .GetSection("Cors:AllowedOrigins")
        .GetChildren()
        .Select(origin => origin.Value)
        .Concat(ParseOrigins(configuration["Cors:AllowedOrigins"]))
        .Concat(ParseOrigins(configuration["CORS_ALLOWED_ORIGINS"]))
        .Concat(ParseOrigins(configuration["ALLOWED_ORIGINS"]));

    return defaultOrigins
        .Concat(configuredOrigins)
        .Where(origin => !string.IsNullOrWhiteSpace(origin))
        .Select(origin => origin!.Trim().Trim('"', '\'').TrimEnd('/'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static string[] ParseOrigins(string? value)
{
    return string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

static RateLimitPartition<string> FixedWindowPolicy(HttpContext context, string configKey, int fallbackPermitLimit)
{
    var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
    var permitLimit = Math.Max(1, configuration.GetValue(configKey, fallbackPermitLimit));
    var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    });
}

namespace Sassoir.Api.Data
{
    public static class DatabaseInitializer
    {
        public static void EnsureSchema(SassoirDbContext db)
        {
            db.Database.ExecuteSqlRaw("""
                create extension if not exists pgcrypto;

                create table if not exists organizations (
                  id uuid primary key default gen_random_uuid(),
                  name text not null,
                  slug text not null unique,
                  status text not null default 'Active',
                  created_at timestamptz not null default now(),
                  updated_at timestamptz not null default now()
                );

                create table if not exists events (
                  id uuid primary key default gen_random_uuid(),
                  organization_id uuid not null references organizations(id) on delete cascade,
                  name text not null,
                  slug text not null unique,
                  event_type text not null default 'Wedding',
                  subtitle text not null default '',
                  description text not null default '',
                  date_label text not null default '',
                  venue_name text not null default '',
                  venue_address text not null default '',
                  seating_assignment_mode text not null default 'table',
                  status text not null default 'Draft',
                  is_public boolean not null default false,
                  published_at timestamptz,
                  created_at timestamptz not null default now(),
                  updated_at timestamptz not null default now()
                );

                create table if not exists event_themes (
                  id uuid primary key default gen_random_uuid(),
                  event_id uuid not null unique references events(id) on delete cascade,
                  logo_text text not null default '',
                  hero_text text not null default '',
                  primary_color text not null default '#D8CFBC',
                  secondary_color text not null default '#565449',
                  background_color text not null default '#FFFBF4',
                  text_color text not null default '#11120D',
                  welcome_title text not null default '',
                  search_input_label text not null default 'Search by name',
                  search_placeholder text not null default 'Search by name',
                  hero_image_url text,
                  logo_url text,
                  updated_at timestamptz not null default now()
                );

                create table if not exists guest_groups (
                  id uuid primary key default gen_random_uuid(),
                  event_id uuid not null references events(id) on delete cascade,
                  name text not null,
                  description text
                );

                create table if not exists event_tables (
                  id uuid primary key default gen_random_uuid(),
                  event_id uuid not null references events(id) on delete cascade,
                  name text not null,
                  code text not null,
                  shape text not null default 'Round',
                  capacity integer not null check (capacity > 0),
                  notes text,
                  zone_name text,
                  floor_plan_x numeric(8, 6),
                  floor_plan_y numeric(8, 6),
                  floor_plan_width numeric(8, 6),
                  floor_plan_height numeric(8, 6),
                  rotation numeric(8, 3) not null default 0,
                  created_at timestamptz not null default now(),
                  updated_at timestamptz not null default now(),
                  unique(event_id, code)
                );

                create table if not exists guests (
                  id uuid primary key default gen_random_uuid(),
                  event_id uuid not null references events(id) on delete cascade,
                  guest_group_id uuid references guest_groups(id) on delete set null,
                  table_id uuid references event_tables(id) on delete set null,
                  first_name text not null default '',
                  last_name text not null default '',
                  display_name text not null,
                  normalized_search_name text not null,
                  public_token text not null unique,
                  group_label text not null default '',
                  seat_number text,
                  directions text not null default '',
                  email text,
                  phone text,
                  notes text,
                  person_count integer not null default 1,
                  status text not null default 'Active',
                  created_at timestamptz not null default now(),
                  updated_at timestamptz not null default now()
                );

                create table if not exists guest_search_aliases (
                  id uuid primary key default gen_random_uuid(),
                  guest_id uuid not null references guests(id) on delete cascade,
                  alias text not null,
                  normalized_alias text not null
                );

                create table if not exists floor_plans (
                  id uuid primary key default gen_random_uuid(),
                  event_id uuid not null references events(id) on delete cascade,
                  name text not null,
                  canvas_aspect_ratio numeric(8, 4) not null default 1.14,
                  version integer not null default 1,
                  is_active boolean not null default true,
                  created_at timestamptz not null default now()
                );

                create table if not exists floor_plan_objects (
                  id text primary key,
                  floor_plan_id uuid not null references floor_plans(id) on delete cascade,
                  linked_table_id uuid references event_tables(id) on delete set null,
                  object_type text not null,
                  label text not null,
                  x numeric(8, 6) not null check (x >= 0 and x <= 1),
                  y numeric(8, 6) not null check (y >= 0 and y <= 1),
                  width numeric(8, 6) not null check (width > 0 and width <= 1),
                  height numeric(8, 6) not null check (height > 0 and height <= 1),
                  rotation numeric(8, 3) not null default 0,
                  shape text not null default 'rect',
                  z_index integer not null default 0,
                  seat_layout text not null default '[]',
                  is_visible boolean not null default true
                );

                create table if not exists guest_messages (
                  id uuid primary key default gen_random_uuid(),
                  event_id uuid not null references events(id) on delete cascade,
                  guest_id uuid not null references guests(id) on delete cascade,
                  message text not null,
                  created_at timestamptz not null default now()
                );

                create table if not exists search_metrics (
                  id uuid primary key default gen_random_uuid(),
                  event_id uuid not null references events(id) on delete cascade,
                  normalized_query text not null,
                  successful boolean not null,
                  created_at timestamptz not null default now()
                );

                create table if not exists contact_submissions (
                  id uuid primary key default gen_random_uuid(),
                  name text not null,
                  email text not null,
                  message text not null,
                  submitted_at_utc timestamptz not null default now()
                );

                create table if not exists app_users (
                  id uuid primary key default gen_random_uuid(),
                  organization_id uuid references organizations(id) on delete set null,
                  first_name text not null,
                  last_name text not null,
                  email text not null unique,
                  password_hash text not null,
                  status text not null default 'Active',
                  is_super_admin boolean not null default false,
                  last_login_at timestamptz,
                  created_at timestamptz not null default now(),
                  updated_at timestamptz not null default now()
                );

                create table if not exists roles (
                  id uuid primary key default gen_random_uuid(),
                  name text not null unique
                );

                create table if not exists user_roles (
                  user_id uuid not null references app_users(id) on delete cascade,
                  role_id uuid not null references roles(id) on delete cascade,
                  primary key (user_id, role_id)
                );

                create index if not exists ix_events_organization_id on events(organization_id);
                create index if not exists ix_events_slug_status on events(slug, status);
                create index if not exists ix_guests_event_search on guests(event_id, normalized_search_name);
                create index if not exists ix_guests_event_status_table on guests(event_id, status, table_id);
                create index if not exists ix_guests_event_public_token on guests(event_id, public_token);
                create index if not exists ix_guest_aliases_guest_alias on guest_search_aliases(guest_id, normalized_alias);
                create index if not exists ix_event_tables_event on event_tables(event_id);
                create index if not exists ix_floor_plans_event_active on floor_plans(event_id, is_active);
                create index if not exists ix_floor_plan_objects_floor_plan_table on floor_plan_objects(floor_plan_id, linked_table_id);
                create index if not exists ix_floor_plan_objects_floor_plan_visible_z on floor_plan_objects(floor_plan_id, is_visible, z_index);
                create index if not exists ix_guest_messages_event_created on guest_messages(event_id, created_at desc);
                create index if not exists ix_search_metrics_event_created on search_metrics(event_id, created_at);
                create index if not exists ix_contact_submissions_submitted_at_utc on contact_submissions(submitted_at_utc desc);
                create index if not exists ix_app_users_email on app_users(email);

                alter table events add column if not exists seating_assignment_mode text not null default 'table';
                alter table floor_plan_objects add column if not exists seat_layout text not null default '[]';
            """);
        }
    }

    public sealed class EventStore
    {
        private readonly SassoirDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<EventStore> _logger;
        private static readonly TimeSpan PublicCacheTtl = TimeSpan.FromMinutes(5);
        private static readonly JsonSerializerOptions SeatLayoutJsonOptions = new(JsonSerializerDefaults.Web);

        public EventStore(SassoirDbContext db, IMemoryCache cache, ILogger<EventStore> logger)
        {
            _db = db;
            _cache = cache;
            _logger = logger;
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

        public PublicEventDto? GetPublishedPublicEvent(string slug)
        {
            var eventEntity = _db.Events
                .AsNoTracking()
                .Include(item => item.Theme)
                .SingleOrDefault(item => item.Slug.ToLower() == slug.ToLower() && item.Status == EventStatus.Published);

            return eventEntity is null ? null : ToPublicEventDto(eventEntity);
        }

        public Task<PublicEventDto?> GetPublishedPublicEventAsync(string slug, CancellationToken cancellationToken)
        {
            var normalizedSlug = NormalizeSlug(slug);
            var cacheKey = PublicEventCacheKey(normalizedSlug);

            return _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = PublicCacheTtl;
                entry.Size = 1;
                _logger.LogDebug("Public event cache miss for {EventSlug}", normalizedSlug);

                return await _db.Events
                    .AsNoTracking()
                    .Where(item => item.Slug == normalizedSlug && item.Status == EventStatus.Published)
                    .Select(item => new PublicEventDto(
                        item.Name,
                        item.Slug,
                        item.EventType,
                        item.SeatingAssignmentMode,
                        item.Subtitle,
                        item.DateLabel,
                        item.VenueName,
                        item.VenueAddress,
                        new EventTheme(
                            item.Theme == null || item.Theme.LogoText == string.Empty ? item.Name : item.Theme.LogoText,
                            item.Theme == null ? string.Empty : item.Theme.HeroText,
                            item.Theme == null ? "#D8CFBC" : item.Theme.PrimaryColor,
                            item.Theme == null ? "#565449" : item.Theme.SecondaryColor,
                            item.Theme == null ? "#FFFBF4" : item.Theme.BackgroundColor,
                            item.Theme == null ? "#11120D" : item.Theme.TextColor,
                            item.Theme == null || item.Theme.WelcomeTitle == string.Empty ? "Welcome" : item.Theme.WelcomeTitle,
                            item.Theme == null || item.Theme.SearchInputLabel == string.Empty ? "Search by name" : item.Theme.SearchInputLabel,
                            item.Theme == null || item.Theme.SearchPlaceholder == string.Empty ? "Search by name" : item.Theme.SearchPlaceholder,
                            item.Theme == null ? null : item.Theme.HeroImageUrl)))
                    .SingleOrDefaultAsync(cancellationToken);
            });
        }

        public FloorPlanDto? GetPublishedFloorPlan(string slug)
        {
            var eventEntity = _db.Events
                .AsNoTracking()
                .Include(item => item.Tables)
                .Include(item => item.FloorPlans)
                    .ThenInclude(item => item.Objects)
                .SingleOrDefault(item => item.Slug.ToLower() == slug.ToLower() && item.Status == EventStatus.Published);

            return eventEntity is null ? null : ToFloorPlanDto(eventEntity);
        }

        public Task<FloorPlanDto?> GetPublishedFloorPlanAsync(string slug, CancellationToken cancellationToken)
        {
            var normalizedSlug = NormalizeSlug(slug);
            var cacheKey = PublicFloorPlanCacheKey(normalizedSlug);

            return _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = PublicCacheTtl;
                entry.Size = 1;
                _logger.LogDebug("Public floor-plan cache miss for {EventSlug}", normalizedSlug);

                var floorPlan = await _db.FloorPlans
                    .AsNoTracking()
                    .Where(item => item.IsActive && item.Event != null && item.Event.Slug == normalizedSlug && item.Event.Status == EventStatus.Published)
                    .OrderByDescending(item => item.Version)
                    .Select(item => new
                    {
                        item.Name,
                        item.CanvasAspectRatio,
                        Objects = item.Objects
                            .Where(floorObject => floorObject.IsVisible)
                            .OrderBy(floorObject => floorObject.ZIndex)
                            .Select(floorObject => new
                            {
                                floorObject.Id,
                                floorObject.ObjectType,
                                floorObject.Label,
                                floorObject.LinkedTableId,
                                floorObject.X,
                                floorObject.Y,
                                floorObject.Width,
                                floorObject.Height,
                                floorObject.Rotation,
                                floorObject.Shape,
                                floorObject.ZIndex,
                                floorObject.SeatLayout,
                                TableCode = _db.EventTables
                                    .Where(table => table.Id == floorObject.LinkedTableId)
                                    .Select(table => table.Code)
                                    .FirstOrDefault(),
                                TableName = _db.EventTables
                                    .Where(table => table.Id == floorObject.LinkedTableId)
                                    .Select(table => table.Name)
                                    .FirstOrDefault(),
                                TableCapacity = _db.EventTables
                                    .Where(table => table.Id == floorObject.LinkedTableId)
                                    .Select(table => (int?)table.Capacity)
                                    .FirstOrDefault()
                            })
                            .ToArray()
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                return floorPlan is null
                    ? null
                    : new FloorPlanDto(
                        floorPlan.Name,
                        floorPlan.CanvasAspectRatio,
                        floorPlan.Objects
                            .Select(item => new FloorPlanObjectDto(
                                item.Id,
                                item.ObjectType,
                                item.Label,
                                null,
                                item.TableCode,
                                item.TableName,
                                item.TableCapacity,
                                item.X,
                                item.Y,
                                item.Width,
                                item.Height,
                                item.Rotation,
                                item.Shape,
                                item.ZIndex,
                                ReadSeatLayout(item.SeatLayout)))
                            .ToArray());
            });
        }

        public GuestSearchResultDto[] SearchPublicGuests(string slug, string normalizedQuery)
        {
            var eventId = _db.Events
                .AsNoTracking()
                .Where(item => item.Slug.ToLower() == slug.ToLower() && item.Status == EventStatus.Published)
                .Select(item => (Guid?)item.Id)
                .SingleOrDefault();
            if (eventId is null) return [];

            var guests = _db.Guests
                .AsNoTracking()
                .Include(item => item.SearchAliases)
                .Where(item => item.EventId == eventId && item.Status == GuestStatus.Active)
                .Where(item =>
                    item.NormalizedSearchName.StartsWith(normalizedQuery) ||
                    item.NormalizedSearchName.Contains(normalizedQuery) ||
                    item.SearchAliases.Any(alias =>
                        alias.NormalizedAlias.StartsWith(normalizedQuery) ||
                        alias.NormalizedAlias.Contains(normalizedQuery)))
                .Take(50)
                .AsEnumerable()
                .Select(guest => new
                {
                    Guest = guest,
                    Rank = SearchNormalizer.Rank(ToGuest(guest), normalizedQuery)
                })
                .Where(match => match.Rank < 99)
                .OrderBy(match => match.Rank)
                .ThenBy(match => match.Guest.DisplayName)
                .Take(5)
                .Select(match => new GuestSearchResultDto(match.Guest.PublicToken, match.Guest.DisplayName, match.Guest.GroupLabel, match.Guest.Notes ?? string.Empty))
                .ToArray();

            return guests;
        }

        public async Task<GuestSearchResultDto[]> SearchPublicGuestsAsync(string slug, string normalizedQuery, CancellationToken cancellationToken)
        {
            var normalizedSlug = NormalizeSlug(slug);
            if (normalizedQuery.Length < 2) return [];

            var eventId = await _db.Events
                .AsNoTracking()
                .Where(item => item.Slug == normalizedSlug && item.Status == EventStatus.Published)
                .Select(item => (Guid?)item.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (eventId is null) return [];

            var matches = await _db.Guests
                .AsNoTracking()
                .Where(item => item.EventId == eventId && item.Status == GuestStatus.Active)
                .Where(item =>
                    item.NormalizedSearchName.StartsWith(normalizedQuery) ||
                    item.NormalizedSearchName.Contains(normalizedQuery) ||
                    item.SearchAliases.Any(alias =>
                        alias.NormalizedAlias.StartsWith(normalizedQuery) ||
                        alias.NormalizedAlias.Contains(normalizedQuery)))
                .OrderBy(item => item.NormalizedSearchName == normalizedQuery ? 0 :
                    item.NormalizedSearchName.StartsWith(normalizedQuery) ? 1 :
                    item.SearchAliases.Any(alias => alias.NormalizedAlias == normalizedQuery) ? 2 :
                    item.SearchAliases.Any(alias => alias.NormalizedAlias.StartsWith(normalizedQuery)) ? 3 :
                    item.NormalizedSearchName.Contains(normalizedQuery) ? 4 : 5)
                .ThenBy(item => item.DisplayName)
                .ThenBy(item => item.PublicToken)
                .Select(item => new
                {
                    item.PublicToken,
                    item.DisplayName,
                    item.NormalizedSearchName,
                    item.Notes,
                    TableCode = item.Table == null ? string.Empty : item.Table.Code,
                    TableName = item.Table == null ? string.Empty : item.Table.Name
                })
                .Take(25)
                .ToArrayAsync(cancellationToken);

            var matchedNames = matches
                .Select(item => item.NormalizedSearchName)
                .Where(item => item != string.Empty)
                .Distinct()
                .ToArray();
            var duplicateKeys = matchedNames.Length == 0
                ? new HashSet<string>(StringComparer.Ordinal)
                : (await _db.Guests
                    .AsNoTracking()
                    .Where(item => item.EventId == eventId && item.Status == GuestStatus.Active && matchedNames.Contains(item.NormalizedSearchName))
                    .GroupBy(item => item.NormalizedSearchName)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToArrayAsync(cancellationToken))
                    .ToHashSet(StringComparer.Ordinal);

            return matches
                .Take(10)
                .Select(item =>
                {
                    var isDuplicate = duplicateKeys.Contains(item.NormalizedSearchName);
                    var note = isDuplicate
                        ? !string.IsNullOrWhiteSpace(item.Notes)
                            ? item.Notes.Trim()
                            : !string.IsNullOrWhiteSpace(item.TableCode)
                                ? $"Table {item.TableCode}{(!string.IsNullOrWhiteSpace(item.TableName) ? $" - {item.TableName}" : string.Empty)}"
                                : string.Empty
                        : string.Empty;
                    return new GuestSearchResultDto(item.PublicToken, item.DisplayName, string.Empty, note);
                })
                .ToArray();
        }

        public SeatResultDto? GetPublicGuestSeat(string slug, string publicToken)
        {
            var guest = _db.Guests
                .AsNoTracking()
                .Include(item => item.Table)
                .Include(item => item.Event)
                    .ThenInclude(item => item!.Theme)
                .SingleOrDefault(item => item.Event != null && item.Event.Slug.ToLower() == slug.ToLower() && item.Event.Status == EventStatus.Published && item.PublicToken == publicToken);
            if (guest?.Event is null) return null;

            var companions = guest.TableId is null
                ? []
                : _db.Guests
                    .AsNoTracking()
                    .Where(item => item.EventId == guest.EventId && item.Status == GuestStatus.Active && item.Id != guest.Id && item.TableId == guest.TableId)
                    .OrderBy(item => item.DisplayName)
                    .Select(item => item.DisplayName)
                    .ToArray();

            return new SeatResultDto(
                guest.DisplayName,
                guest.GroupLabel,
                guest.Table?.Code ?? string.Empty,
                guest.Table?.Name ?? string.Empty,
                guest.SeatNumber,
                guest.Directions,
                companions,
                ToPublicEventDto(guest.Event));
        }

        public async Task<PublicSeatResultDto?> GetPublicGuestSeatAsync(string slug, string publicToken, CancellationToken cancellationToken)
        {
            var normalizedSlug = NormalizeSlug(slug);
            var guest = await _db.Guests
                .AsNoTracking()
                .Where(item => item.PublicToken == publicToken && item.Event != null && item.Event.Slug == normalizedSlug && item.Event.Status == EventStatus.Published)
                .Select(item => new
                {
                    item.Id,
                    item.EventId,
                    item.TableId,
                    item.PublicToken,
                    item.DisplayName,
                    item.GroupLabel,
                    item.SeatNumber,
                    item.Directions,
                    TableCode = item.Table == null ? string.Empty : item.Table.Code,
                    TableName = item.Table == null ? string.Empty : item.Table.Name
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (guest is null) return null;

            var companions = guest.TableId is null
                ? []
                : await _db.Guests
                    .AsNoTracking()
                    .Where(item => item.EventId == guest.EventId && item.Status == GuestStatus.Active && item.Id != guest.Id && item.TableId == guest.TableId)
                    .OrderBy(item => item.DisplayName)
                    .Select(item => item.DisplayName)
                    .Take(20)
                    .ToArrayAsync(cancellationToken);

            var publicEvent = await GetPublishedPublicEventAsync(normalizedSlug, cancellationToken);
            if (publicEvent is null) return null;

            var floorPlan = await GetPublishedFloorPlanAsync(normalizedSlug, cancellationToken);
            var highlightedObjectId = floorPlan?.Objects
                .Where(item => string.Equals(item.TableCode, guest.TableCode, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Id)
                .FirstOrDefault();

            return new PublicSeatResultDto(
                guest.PublicToken,
                guest.DisplayName,
                guest.GroupLabel,
                guest.TableCode,
                guest.TableName,
                guest.SeatNumber,
                guest.Directions,
                companions,
                publicEvent,
                floorPlan,
                highlightedObjectId);
        }

        public GuestFloorPlanDto? GetPublicGuestFloorPlan(string slug, string publicToken)
        {
            var guest = _db.Guests
                .AsNoTracking()
                .Include(item => item.Event)
                .SingleOrDefault(item => item.Event != null && item.Event.Slug.ToLower() == slug.ToLower() && item.Event.Status == EventStatus.Published && item.PublicToken == publicToken);
            if (guest?.Event is null) return null;

            var eventEntity = _db.Events
                .AsNoTracking()
                .Include(item => item.Tables)
                .Include(item => item.FloorPlans)
                    .ThenInclude(item => item.Objects)
                .Single(item => item.Id == guest.EventId);

            var highlightedId = eventEntity.FloorPlans
                .Where(item => item.IsActive)
                .OrderByDescending(item => item.Version)
                .SelectMany(item => item.Objects)
                .Where(item => item.LinkedTableId == guest.TableId)
                .Select(item => item.Id)
                .FirstOrDefault() ?? $"table-{guest.TableId}";

            return new GuestFloorPlanDto(ToFloorPlanDto(eventEntity), highlightedId);
        }

        public async Task<GuestFloorPlanDto?> GetPublicGuestFloorPlanAsync(string slug, string publicToken, CancellationToken cancellationToken)
        {
            var seat = await GetPublicGuestSeatAsync(slug, publicToken, cancellationToken);
            if (seat?.FloorPlan is null) return null;

            return new GuestFloorPlanDto(seat.FloorPlan, seat.HighlightedObjectId ?? string.Empty);
        }

        public EventDetails? GetEvent(Guid id)
        {
            var eventEntity = EventQuery().SingleOrDefault(item => item.Id == id);
            return eventEntity is null ? null : ToEventDetails(eventEntity);
        }

        public async Task<FloorPlanDto?> GetAdminFloorPlanAsync(Guid eventId, CancellationToken cancellationToken)
        {
            var eventExists = await _db.Events
                .AsNoTracking()
                .AnyAsync(item => item.Id == eventId, cancellationToken);
            if (!eventExists) return null;

            var floorPlan = await _db.FloorPlans
                .AsNoTracking()
                .Where(item => item.EventId == eventId && item.IsActive)
                .OrderByDescending(item => item.Version)
                .Select(item => new
                {
                    item.Id,
                    item.Name,
                    item.CanvasAspectRatio
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (floorPlan is null)
            {
                return new FloorPlanDto("Venue layout", 1.14m, []);
            }

            var floorObjects = await _db.FloorPlanObjects
                .AsNoTracking()
                .Where(item => item.FloorPlanId == floorPlan.Id && item.IsVisible)
                .OrderBy(item => item.ZIndex)
                .Select(item => new
                {
                    item.Id,
                    item.ObjectType,
                    item.Label,
                    item.LinkedTableId,
                    item.X,
                    item.Y,
                    item.Width,
                    item.Height,
                    item.Rotation,
                    item.Shape,
                    item.ZIndex,
                    item.SeatLayout
                })
                .ToArrayAsync(cancellationToken);

            var linkedTableIds = floorObjects
                .Select(item => item.LinkedTableId)
                .Where(item => item.HasValue)
                .Select(item => item!.Value)
                .Distinct()
                .ToArray();

            var tableLookup = new Dictionary<Guid, (string Code, string Name, int Capacity)>();
            if (linkedTableIds.Length > 0)
            {
                var linkedTables = await _db.EventTables
                    .AsNoTracking()
                    .Where(item => item.EventId == eventId && linkedTableIds.Contains(item.Id))
                    .Select(item => new
                    {
                        item.Id,
                        item.Code,
                        item.Name,
                        item.Capacity
                    })
                    .ToArrayAsync(cancellationToken);

                tableLookup = linkedTables.ToDictionary(item => item.Id, item => (item.Code, item.Name, item.Capacity));
            }

            return new FloorPlanDto(
                floorPlan.Name,
                floorPlan.CanvasAspectRatio,
                floorObjects
                    .Select(item =>
                    {
                        var linkedTable = item.LinkedTableId.HasValue && tableLookup.TryGetValue(item.LinkedTableId.Value, out var table)
                            ? table
                            : default;

                        return new FloorPlanObjectDto(
                            item.Id,
                            item.ObjectType,
                            item.Label,
                            item.LinkedTableId,
                            linkedTable.Code,
                            linkedTable.Name,
                            linkedTable.Capacity == 0 ? null : linkedTable.Capacity,
                            item.X,
                            item.Y,
                            item.Width,
                            item.Height,
                            item.Rotation,
                            item.Shape,
                            item.ZIndex,
                            ReadSeatLayout(item.SeatLayout));
                    })
                    .ToArray());
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
                    item.SeatingAssignmentMode,
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
                    item.SeatingAssignmentMode == "seat"
                        ? item.Guests.Count(guest => guest.Status != GuestStatus.Archived && guest.TableId != null && guest.SeatNumber != null && guest.SeatNumber != string.Empty)
                        : item.Guests.Count(guest => guest.Status != GuestStatus.Archived && guest.TableId != null)))
                .ToArray();
        }

        public async Task<PaginatedResponse<AdminEventDto>> GetAdminEventsPageAsync(string? search, string? status, int? page, int? pageSize, CancellationToken cancellationToken)
        {
            var paging = NormalizePaging(page, pageSize);
            var normalizedSearch = SearchNormalizer.Normalize(search);
            var query = _db.Events.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                query = query.Where(item =>
                    item.Name.ToLower().Contains(normalizedSearch) ||
                    item.Slug.ToLower().Contains(normalizedSearch) ||
                    item.VenueName.ToLower().Contains(normalizedSearch));
            }

            if (Enum.TryParse<EventStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(item => item.Status == parsedStatus);
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(item => item.CreatedAt)
                .Skip((paging.Page - 1) * paging.PageSize)
                .Take(paging.PageSize)
                .Select(item => new AdminEventDto(
                    item.Id,
                    item.Name,
                    item.Slug,
                    item.EventType,
                    item.SeatingAssignmentMode,
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
                    item.SeatingAssignmentMode == "seat"
                        ? item.Guests.Count(guest => guest.Status != GuestStatus.Archived && guest.TableId != null && guest.SeatNumber != null && guest.SeatNumber != string.Empty)
                        : item.Guests.Count(guest => guest.Status != GuestStatus.Archived && guest.TableId != null)))
                .ToArrayAsync(cancellationToken);

            return new PaginatedResponse<AdminEventDto>(items, paging.Page, paging.PageSize, totalCount);
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
                SeatingAssignmentMode = NormalizeSeatingAssignmentMode(request.SeatingAssignmentMode),
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
            InvalidatePublicCache(slug);
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

            var previousSlug = eventEntity.Slug;
            eventEntity.Name = request.Name.Trim();
            eventEntity.Slug = slug;
            eventEntity.EventType = NormalizeEventType(request.EventType);
            eventEntity.Subtitle = request.Subtitle?.Trim() ?? string.Empty;
            eventEntity.DateLabel = request.DateLabel?.Trim() ?? string.Empty;
            eventEntity.VenueName = request.VenueName?.Trim() ?? string.Empty;
            eventEntity.VenueAddress = request.VenueAddress?.Trim() ?? string.Empty;
            eventEntity.SeatingAssignmentMode = NormalizeSeatingAssignmentMode(request.SeatingAssignmentMode);
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
            InvalidatePublicCache(previousSlug);
            InvalidatePublicCache(slug);
            return (GetEvent(id), null);
        }

        public bool DeleteEvent(Guid id)
        {
            var eventEntity = _db.Events.SingleOrDefault(item => item.Id == id);
            if (eventEntity is null) return false;

            _db.Events.Remove(eventEntity);
            _db.SaveChanges();
            InvalidatePublicCache(eventEntity.Slug);
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
            InvalidatePublicCache(eventEntity.Slug);

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

        public async Task<PaginatedResponse<AdminGuestDto>> GetAdminGuestsPageAsync(Guid eventId, string? search, string? status, string? tableId, int? page, int? pageSize, CancellationToken cancellationToken)
        {
            var paging = NormalizePaging(page, pageSize);
            var normalizedSearch = SearchNormalizer.Normalize(search);
            var query = _db.Guests
                .AsNoTracking()
                .Where(item => item.EventId == eventId);

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                query = query.Where(item =>
                    item.NormalizedSearchName.Contains(normalizedSearch) ||
                    item.FirstName.ToLower().Contains(normalizedSearch) ||
                    item.LastName.ToLower().Contains(normalizedSearch) ||
                    item.DisplayName.ToLower().Contains(normalizedSearch) ||
                    (item.Notes != null && item.Notes.ToLower().Contains(normalizedSearch)) ||
                    (item.Table != null && (item.Table.Code.ToLower().Contains(normalizedSearch) || item.Table.Name.ToLower().Contains(normalizedSearch))));
            }

            if (Enum.TryParse<GuestStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(item => item.Status == parsedStatus);
            }
            else if (string.Equals(status, "Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(item => item.TableId == null && item.Status != GuestStatus.Archived);
            }

            if (Guid.TryParse(tableId, out var parsedTableId))
            {
                query = query.Where(item => item.TableId == parsedTableId);
            }
            else if (string.Equals(tableId, "Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(item => item.TableId == null);
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderBy(item => item.DisplayName)
                .ThenBy(item => item.Id)
                .Skip((paging.Page - 1) * paging.PageSize)
                .Take(paging.PageSize)
                .Select(item => new AdminGuestDto(
                    item.Id,
                    item.FirstName,
                    item.LastName,
                    item.DisplayName,
                    item.Notes ?? string.Empty,
                    item.PersonCount < 1 ? 1 : item.PersonCount,
                    item.TableId,
                    item.Table == null ? string.Empty : item.Table.Code,
                    item.Table == null ? string.Empty : item.Table.Name,
                    item.SeatNumber,
                    item.Status,
                    false))
                .ToArrayAsync(cancellationToken);

            return new PaginatedResponse<AdminGuestDto>(items, paging.Page, paging.PageSize, totalCount);
        }

        public (AdminGuestDto? Guest, string? Error) CreateGuest(Guid eventId, AdminGuestCreateRequest request)
        {
            if (!_db.Events.Any(item => item.Id == eventId)) return (null, "not-found");

            var guest = BuildGuest(eventId, request.FirstName, request.LastName, request.DisplayName, request.Notes, request.PersonCount);
            if (string.IsNullOrWhiteSpace(guest.DisplayName)) return (null, "Display name is required.");
            guest.Status = request.Status ?? GuestStatus.Active;

            var assignment = ValidateGuestAssignment(eventId, null, guest.Status == GuestStatus.Archived ? null : request.TableId, request.SeatNumber, guest.Status, guest.PersonCount);
            if (assignment.Error is not null) return (null, assignment.Error);
            guest.TableId = guest.Status == GuestStatus.Archived ? null : request.TableId;
            guest.SeatNumber = guest.TableId is null ? null : assignment.SeatNumber;

            _db.Guests.Add(guest);
            _db.SaveChanges();
            guest.Table = assignment.Table;
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

            var normalizedPersonCount = NormalizePersonCount(request.PersonCount);
            var finalTableId = request.Status == GuestStatus.Archived ? null : request.TableId;
            var assignment = ValidateGuestAssignment(eventId, guestId, finalTableId, request.SeatNumber, request.Status, normalizedPersonCount);
            if (assignment.Error is not null) return (null, assignment.Error);

            guest.FirstName = firstName;
            guest.LastName = lastName;
            guest.DisplayName = displayName;
            guest.NormalizedSearchName = SearchNormalizer.Normalize(displayName);
            guest.Notes = request.Notes?.Trim();
            guest.PersonCount = normalizedPersonCount;
            guest.TableId = finalTableId;
            guest.SeatNumber = finalTableId is null ? null : assignment.SeatNumber;
            guest.Status = request.Status;
            guest.UpdatedAt = DateTimeOffset.UtcNow;
            _db.SaveChanges();

            guest.Table = guest.TableId is null ? null : assignment.Table;
            return (ToAdminGuestDto(guest), null);
        }

        public (AdminGuestDto? Guest, string? Error) ArchiveGuest(Guid eventId, Guid guestId)
        {
            var guest = _db.Guests.Include(item => item.Table).SingleOrDefault(item => item.Id == guestId && item.EventId == eventId);
            if (guest is null) return (null, "not-found");

            guest.Status = GuestStatus.Archived;
            guest.TableId = null;
            guest.SeatNumber = null;
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

        public (int DeletedCount, string? Error) BulkDeleteGuests(Guid eventId, IReadOnlyList<Guid> guestIds)
        {
            if (!_db.Events.Any(item => item.Id == eventId)) return (0, "not-found");

            var uniqueGuestIds = guestIds.Distinct().ToArray();
            if (uniqueGuestIds.Length == 0) return (0, null);

            var guests = _db.Guests
                .Where(item => item.EventId == eventId && uniqueGuestIds.Contains(item.Id))
                .ToArray();
            if (guests.Length != uniqueGuestIds.Length) return (0, "not-found");

            _db.Guests.RemoveRange(guests);
            _db.SaveChanges();
            return (guests.Length, null);
        }

        public (AdminGuestDto? Guest, string? Error) AssignGuestToTable(Guid eventId, Guid guestId, Guid? tableId, string? seatNumber)
        {
            var guest = _db.Guests.Include(item => item.Table).SingleOrDefault(item => item.Id == guestId && item.EventId == eventId);
            if (guest is null) return (null, "not-found");
            if (guest.Status == GuestStatus.Archived) return (null, "Archived guests cannot be assigned to tables.");

            var assignmentResult = ValidateGuestAssignment(eventId, guestId, tableId, seatNumber, guest.Status, guest.PersonCount);
            if (assignmentResult.Error is not null) return (null, assignmentResult.Error);

            guest.TableId = tableId;
            guest.SeatNumber = tableId is null ? null : assignmentResult.SeatNumber;
            guest.UpdatedAt = DateTimeOffset.UtcNow;
            _db.SaveChanges();

            guest.Table = assignmentResult.Table;
            return (ToAdminGuestDto(guest), null);
        }

        public (IReadOnlyList<AdminGuestDto>? Guests, string? Error) BulkAssignGuestsToTable(Guid eventId, IReadOnlyList<Guid> guestIds, Guid? tableId)
        {
            if (!_db.Events.Any(item => item.Id == eventId)) return (null, "not-found");

            var uniqueGuestIds = guestIds.Distinct().ToArray();
            if (uniqueGuestIds.Length == 0) return ([], null);

            var guests = _db.Guests
                .Include(item => item.Table)
                .Where(item => item.EventId == eventId && uniqueGuestIds.Contains(item.Id))
                .ToArray();
            if (guests.Length != uniqueGuestIds.Length) return (null, "not-found");
            if (guests.Any(item => item.Status == GuestStatus.Archived)) return (null, "Archived guests cannot be assigned to tables.");
            if (tableId is not null && GetEventSeatingAssignmentMode(eventId) == "seat") return (null, "Seat-based events assign seats one guest at a time.");

            EventTableEntity? table = null;
            if (tableId is not null)
            {
                table = _db.EventTables.Include(item => item.Guests).SingleOrDefault(item => item.Id == tableId && item.EventId == eventId);
                if (table is null) return (null, "not-found");

                var incomingAssigned = CountSeatedPeople(guests.Where(item => item.TableId != tableId));
                var currentlyAssigned = CountSeatedPeople(table.Guests.Where(item => !uniqueGuestIds.Contains(item.Id)));
                if (currentlyAssigned + incomingAssigned > table.Capacity)
                {
                    return (null, $"Table {table.Code} does not have enough open seats.");
                }
            }

            foreach (var guest in guests)
            {
                guest.TableId = tableId;
                guest.SeatNumber = null;
                guest.UpdatedAt = DateTimeOffset.UtcNow;
                guest.Table = table;
            }

            _db.SaveChanges();
            return (guests.Select(item => ToAdminGuestDto(item)).ToArray(), null);
        }

        public (AdminGuestImportPreviewDto? Preview, string? Error) PreviewGuestImport(Guid eventId, IReadOnlyList<AdminGuestImportRow> rows)
        {
            if (!_db.Events.Any(item => item.Id == eventId)) return (null, "not-found");

            var seatingMode = GetEventSeatingAssignmentMode(eventId);
            var tables = _db.EventTables
                .AsNoTracking()
                .Where(item => item.EventId == eventId)
                .ToArray();
            var tablesByCode = tables.ToDictionary(item => item.Code.Trim(), StringComparer.OrdinalIgnoreCase);
            var tablesByName = tables
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var existingKeys = _db.Guests
                .AsNoTracking()
                .Where(item => item.EventId == eventId)
                .Select(item => item.NormalizedSearchName)
                .Where(item => item != string.Empty)
                .ToHashSet(StringComparer.Ordinal);
            var assignedCountsByTable = _db.Guests
                .AsNoTracking()
                .Where(item => item.EventId == eventId && item.TableId != null && (item.Status == GuestStatus.Active || item.Status == GuestStatus.CheckedIn))
                .GroupBy(item => item.TableId!.Value)
                .ToDictionary(
                    group => group.Key,
                    group => seatingMode == "seat" ? group.Count() : group.Sum(item => Math.Max(1, item.PersonCount)));
            var occupiedSeats = _db.Guests
                .AsNoTracking()
                .Where(item => item.EventId == eventId && item.TableId != null && item.SeatNumber != null && item.SeatNumber != string.Empty && (item.Status == GuestStatus.Active || item.Status == GuestStatus.CheckedIn))
                .Select(item => $"{item.TableId}:{item.SeatNumber}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var seenKeys = new Dictionary<string, int>(StringComparer.Ordinal);
            var seenSeatKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var previewRows = rows
                .Select((row, index) => BuildImportPreviewRow(row, index + 2, existingKeys, seenKeys, tablesByCode, tablesByName, assignedCountsByTable, occupiedSeats, seenSeatKeys, seatingMode))
                .ToArray();

            return (new AdminGuestImportPreviewDto(previewRows, previewRows.Count(item => item.Errors.Length > 0), previewRows.Count(item => item.IsDuplicate)), null);
        }

        public (AdminGuestImportPreviewDto? Preview, string? Error) PreviewGuestImportCsv(Guid eventId, string csv)
        {
            var rows = ParseGuestImportCsv(csv);
            if (rows.Length == 0) return (null, "No guest rows were found in the import file.");

            return PreviewGuestImport(eventId, rows);
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

            var reservedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var guests = preview.Preview.Rows
                .Select(item =>
                {
                    var guest = BuildGuest(eventId, item.FirstName, item.LastName, item.DisplayName, item.Notes, item.PersonCount, reservedTokens);
                    guest.TableId = item.TableId;
                    guest.SeatNumber = item.TableId is null ? null : item.SeatNumber;
                    return guest;
                })
                .ToArray();

            _db.Guests.AddRange(guests);
            _db.SaveChanges();
            return (guests.Select(item => ToAdminGuestDto(item)).ToArray(), null);
        }

        public (IReadOnlyList<AdminGuestDto>? Guests, string? Error) ImportGuestsCsv(Guid eventId, string csv)
        {
            var rows = ParseGuestImportCsv(csv);
            if (rows.Length == 0) return (null, "No guest rows were found in the import file.");

            return ImportGuests(eventId, rows);
        }

        public string ExportGuestsCsv(Guid eventId)
        {
            static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

            var guests = GetAdminGuests(eventId);
            var builder = new StringBuilder();
            builder.AppendLine("First Name,Last Name,Display Name,Person Count,Notes,Status,Table Number,Table Name,Seat Number");
            foreach (var guest in guests)
            {
                builder.AppendLine(string.Join(',', [
                    Csv(guest.FirstName),
                    Csv(guest.LastName),
                    Csv(guest.DisplayName),
                    Csv(guest.PersonCount.ToString(CultureInfo.InvariantCulture)),
                    Csv(guest.Notes),
                    Csv(guest.Status.ToString()),
                    Csv(guest.TableCode),
                    Csv(guest.TableName),
                    Csv(guest.SeatNumber ?? string.Empty)
                ]));
            }

            return builder.ToString();
        }

        public IReadOnlyList<AdminTableDto> GetAdminTables(Guid eventId)
        {
            return _db.EventTables
                .AsNoTracking()
                .Where(item => item.EventId == eventId)
                .OrderBy(item => item.Code)
                .Select(item => new AdminTableDto(
                    item.Id,
                    item.Name,
                    item.Code,
                    item.Capacity,
                    item.Event != null && item.Event.SeatingAssignmentMode == "seat"
                        ? item.Guests.Count(guest => guest.Status == GuestStatus.Active || guest.Status == GuestStatus.CheckedIn)
                        : item.Guests
                            .Where(guest => guest.Status == GuestStatus.Active || guest.Status == GuestStatus.CheckedIn)
                            .Sum(guest => guest.PersonCount < 1 ? 1 : guest.PersonCount),
                    item.Shape.ToLower() == "square" ? "square" :
                        item.Shape.ToLower() == "rectangle" ? "rectangle" :
                        item.Shape.ToLower() == "tear" ? "tear" : "round",
                    item.Notes ?? string.Empty))
                .ToArray();
        }

        public async Task<PaginatedResponse<AdminTableDto>> GetAdminTablesPageAsync(Guid eventId, string? search, int? page, int? pageSize, CancellationToken cancellationToken)
        {
            var paging = NormalizePaging(page, pageSize);
            var normalizedSearch = SearchNormalizer.Normalize(search);
            var query = _db.EventTables
                .AsNoTracking()
                .Where(item => item.EventId == eventId);

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                query = query.Where(item =>
                    item.Code.ToLower().Contains(normalizedSearch) ||
                    item.Name.ToLower().Contains(normalizedSearch) ||
                    (item.Notes != null && item.Notes.ToLower().Contains(normalizedSearch)));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderBy(item => item.Code)
                .ThenBy(item => item.Id)
                .Skip((paging.Page - 1) * paging.PageSize)
                .Take(paging.PageSize)
                .Select(item => new AdminTableDto(
                    item.Id,
                    item.Name,
                    item.Code,
                    item.Capacity,
                    item.Event != null && item.Event.SeatingAssignmentMode == "seat"
                        ? item.Guests.Count(guest => guest.Status == GuestStatus.Active || guest.Status == GuestStatus.CheckedIn)
                        : item.Guests
                            .Where(guest => guest.Status == GuestStatus.Active || guest.Status == GuestStatus.CheckedIn)
                            .Sum(guest => guest.PersonCount < 1 ? 1 : guest.PersonCount),
                    item.Shape.ToLower() == "square" ? "square" :
                        item.Shape.ToLower() == "rectangle" ? "rectangle" :
                        item.Shape.ToLower() == "tear" ? "tear" : "round",
                    item.Notes ?? string.Empty))
                .ToArrayAsync(cancellationToken);

            return new PaginatedResponse<AdminTableDto>(items, paging.Page, paging.PageSize, totalCount);
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
                Shape = NormalizeTableShape(request.Shape),
                Notes = request.Notes?.Trim(),
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
                Label = string.IsNullOrWhiteSpace(table.Name) ? $"Table {number}" : table.Name,
                X = 0.12m + (_db.EventTables.Count(item => item.EventId == eventId) % 4) * 0.18m,
                Y = 0.24m,
                Width = ShapeDefaultWidth(table.Shape),
                Height = ShapeDefaultHeight(table.Shape),
                Shape = ToFloorShape(table.Shape),
                ZIndex = 10,
                SeatLayout = "[]",
                IsVisible = true
            });

            _db.SaveChanges();
            InvalidatePublicCache(eventId);
            return (ToAdminTableDto(table, GetEventSeatingAssignmentMode(eventId)), null);
        }

        public (AdminTableDto? Table, string? Error) UpdateTable(Guid eventId, Guid tableId, AdminTableUpsertRequest request)
        {
            var table = _db.EventTables
                .Include(item => item.Guests)
                .SingleOrDefault(item => item.Id == tableId && item.EventId == eventId);
            if (table is null) return (null, "not-found");

            var number = request.Number.Trim();
            if (_db.EventTables.Any(item => item.EventId == eventId && item.Id != tableId && item.Code == number))
            {
                return (null, "A table with this number already exists.");
            }

            var capacity = Math.Max(1, request.MaximumCapacity);
            var seatingMode = GetEventSeatingAssignmentMode(eventId);
            var assignedCount = CountAssignedSeatsOrPeople(table.Guests, seatingMode);
            if (capacity < assignedCount)
            {
                return (null, $"Capacity cannot be below the assigned {(seatingMode == "seat" ? "seat" : "person")} count ({assignedCount}).");
            }

            var nextShape = NormalizeTableShape(request.Shape);
            var shapeChanged = !string.Equals(NormalizeTableShape(table.Shape), nextShape, StringComparison.OrdinalIgnoreCase);
            table.Name = request.Name.Trim();
            table.Code = number;
            table.Capacity = capacity;
            table.Shape = nextShape;
            table.Notes = request.Notes?.Trim();
            table.UpdatedAt = DateTimeOffset.UtcNow;

            var floorObjects = _db.FloorPlanObjects.Where(item => item.LinkedTableId == tableId).ToArray();
            foreach (var floorObject in floorObjects)
            {
                floorObject.Label = string.IsNullOrWhiteSpace(table.Name) ? $"Table {number}" : table.Name;
                floorObject.Shape = ToFloorShape(table.Shape);
                floorObject.Width = shapeChanged ? ShapeDefaultWidth(table.Shape) : request.Width is null ? floorObject.Width : Math.Clamp(request.Width.Value, 0.04m, 1m);
                floorObject.Height = shapeChanged ? ShapeDefaultHeight(table.Shape) : request.Height is null ? floorObject.Height : Math.Clamp(request.Height.Value, 0.04m, 1m);
            }

            _db.SaveChanges();
            InvalidatePublicCache(eventId);
            return (ToAdminTableDto(table, seatingMode), null);
        }

        public bool DeleteTable(Guid eventId, Guid tableId)
        {
            var table = _db.EventTables.SingleOrDefault(item => item.Id == tableId && item.EventId == eventId);
            if (table is null) return false;

            var guests = _db.Guests.Where(item => item.EventId == eventId && item.TableId == tableId).ToArray();
            foreach (var guest in guests)
            {
                guest.TableId = null;
                guest.SeatNumber = null;
                guest.UpdatedAt = DateTimeOffset.UtcNow;
            }

            var floorObjects = _db.FloorPlanObjects.Where(item => item.LinkedTableId == tableId).ToArray();
            _db.FloorPlanObjects.RemoveRange(floorObjects);
            _db.EventTables.Remove(table);
            _db.SaveChanges();
            InvalidatePublicCache(eventId);
            return true;
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
                    Rotation = NormalizeRotation(item.Rotation),
                    Shape = item.Shape,
                    ZIndex = item.ZIndex,
                    SeatLayout = SerializeSeatLayout(item.SeatLayout),
                    IsVisible = true
                });
            }

            floorPlan.Version += 1;
            _db.SaveChanges();
            InvalidatePublicCache(eventId);
            return GetEvent(eventId)?.FloorPlan;
        }

        public async Task<FloorPlanDto?> SaveFloorPlanAsync(Guid eventId, FloorPlanSaveRequest request, CancellationToken cancellationToken)
        {
            if (!await _db.Events.AsNoTracking().AnyAsync(item => item.Id == eventId, cancellationToken)) return null;

            var floorPlan = await _db.FloorPlans
                .Include(item => item.Objects)
                .SingleOrDefaultAsync(item => item.EventId == eventId && item.IsActive, cancellationToken);

            if (floorPlan is null)
            {
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
            }

            _db.FloorPlanObjects.RemoveRange(floorPlan.Objects);

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
                    Rotation = NormalizeRotation(item.Rotation),
                    Shape = item.Shape,
                    ZIndex = item.ZIndex,
                    SeatLayout = SerializeSeatLayout(item.SeatLayout),
                    IsVisible = true
                });
            }

            floorPlan.Version += 1;
            await _db.SaveChangesAsync(cancellationToken);
            InvalidatePublicCache(eventId);
            return await GetAdminFloorPlanAsync(eventId, cancellationToken);
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

        public async Task<bool> SaveMessageAsync(string slug, string publicToken, string message, CancellationToken cancellationToken)
        {
            var normalizedSlug = NormalizeSlug(slug);
            var guest = await _db.Guests
                .AsNoTracking()
                .Where(item => item.Event != null && item.Event.Slug == normalizedSlug && item.Event.Status == EventStatus.Published && item.PublicToken == publicToken)
                .Select(item => new { item.Id, item.EventId })
                .SingleOrDefaultAsync(cancellationToken);
            if (guest is null) return false;

            _db.GuestMessages.Add(new GuestMessageEntity
            {
                Id = Guid.NewGuid(),
                EventId = guest.EventId,
                GuestId = guest.Id,
                Message = message,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        public IReadOnlyList<AdminGuestMessageDto> GetGuestMessages(Guid eventId)
        {
            return _db.GuestMessages
                .AsNoTracking()
                .Include(item => item.Guest)
                .Where(item => item.EventId == eventId)
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new AdminGuestMessageDto(
                    item.Id,
                    item.Guest == null ? "Guest" : item.Guest.DisplayName,
                    item.Message,
                    item.CreatedAt))
                .ToArray();
        }

        public async Task<PaginatedResponse<AdminGuestMessageDto>> GetGuestMessagesPageAsync(Guid eventId, int? page, int? pageSize, CancellationToken cancellationToken)
        {
            var paging = NormalizePaging(page, pageSize);
            var query = _db.GuestMessages
                .AsNoTracking()
                .Where(item => item.EventId == eventId);

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .Skip((paging.Page - 1) * paging.PageSize)
                .Take(paging.PageSize)
                .Select(item => new AdminGuestMessageDto(
                    item.Id,
                    item.Guest == null ? "Guest" : item.Guest.DisplayName,
                    item.Message,
                    item.CreatedAt))
                .ToArrayAsync(cancellationToken);

            return new PaginatedResponse<AdminGuestMessageDto>(items, paging.Page, paging.PageSize, totalCount);
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

        public async Task TrackSearchAsync(string slug, string normalizedQuery, bool successful, CancellationToken cancellationToken)
        {
            var normalizedSlug = NormalizeSlug(slug);
            var eventId = await _db.Events
                .AsNoTracking()
                .Where(item => item.Slug == normalizedSlug)
                .Select(item => (Guid?)item.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (eventId is null) return;

            _db.SearchMetrics.Add(new SearchMetricEntity
            {
                Id = Guid.NewGuid(),
                EventId = eventId.Value,
                NormalizedQuery = normalizedQuery,
                Successful = successful,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        private void InvalidatePublicCache(Guid eventId)
        {
            var slug = _db.Events
                .AsNoTracking()
                .Where(item => item.Id == eventId)
                .Select(item => item.Slug)
                .SingleOrDefault();
            if (!string.IsNullOrWhiteSpace(slug))
            {
                InvalidatePublicCache(slug);
            }
        }

        private void InvalidatePublicCache(string slug)
        {
            var normalizedSlug = NormalizeSlug(slug);
            _cache.Remove(PublicEventCacheKey(normalizedSlug));
            _cache.Remove(PublicFloorPlanCacheKey(normalizedSlug));
            _logger.LogInformation("Invalidated public event cache for {EventSlug}", normalizedSlug);
        }

        private static string PublicEventCacheKey(string slug) => $"public:event:{slug}";

        private static string PublicFloorPlanCacheKey(string slug) => $"public:floor-plan:{slug}";

        private static string NormalizeSlug(string slug) => slug.Trim().ToLowerInvariant();

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

        private static PublicEventDto ToPublicEventDto(EventEntity eventEntity)
        {
            var theme = ToEventTheme(eventEntity);
            return new PublicEventDto(
                eventEntity.Name,
                eventEntity.Slug,
                eventEntity.EventType,
                eventEntity.SeatingAssignmentMode,
                eventEntity.Subtitle,
                eventEntity.DateLabel,
                eventEntity.VenueName,
                eventEntity.VenueAddress,
                theme);
        }

        private static EventTheme ToEventTheme(EventEntity eventEntity)
        {
            return eventEntity.Theme is null
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
        }

        private static FloorPlanDto ToFloorPlanDto(EventEntity eventEntity)
        {
            var floorPlan = eventEntity.FloorPlans
                .Where(item => item.IsActive)
                .OrderByDescending(item => item.Version)
                .FirstOrDefault();

            return new FloorPlanDto(
                floorPlan?.Name ?? "Venue layout",
                floorPlan?.CanvasAspectRatio ?? 1.14m,
                floorPlan?.Objects
                    .Where(item => item.IsVisible)
                    .OrderBy(item => item.ZIndex)
                    .Select(item =>
                    {
                        var linkedTable = item.LinkedTableId is null ? null : eventEntity.Tables.FirstOrDefault(table => table.Id == item.LinkedTableId);
                        return new FloorPlanObjectDto(
                            item.Id,
                            item.ObjectType,
                            item.Label,
                            item.LinkedTableId,
                            linkedTable?.Code,
                            linkedTable?.Name,
                            linkedTable?.Capacity,
                            item.X,
                            item.Y,
                            item.Width,
                            item.Height,
                            item.Rotation,
                            item.Shape,
                            item.ZIndex,
                            ReadSeatLayout(item.SeatLayout));
                    })
                    .ToArray() ?? []);
        }

        private static EventDetails ToEventDetails(EventEntity eventEntity)
        {
            var theme = ToEventTheme(eventEntity);

            return new EventDetails(
                eventEntity.Id,
                eventEntity.Name,
                eventEntity.Slug,
                eventEntity.EventType,
                NormalizeSeatingAssignmentMode(eventEntity.SeatingAssignmentMode),
                eventEntity.Subtitle,
                eventEntity.DateLabel,
                eventEntity.VenueName,
                eventEntity.VenueAddress,
                eventEntity.Status,
                theme,
                ToFloorPlanDto(eventEntity),
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

        private GuestEntity BuildGuest(Guid eventId, string? firstNameValue, string? lastNameValue, string? displayNameValue, string? notesValue, int? personCountValue, HashSet<string>? reservedTokens = null)
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
                PublicToken = BuildGuestToken(displayName, reservedTokens),
                Notes = notesValue?.Trim(),
                PersonCount = NormalizePersonCount(personCountValue),
                Status = GuestStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        private string BuildGuestToken(string displayName, HashSet<string>? reservedTokens = null)
        {
            var tokenBase = Slugify(displayName);
            var token = $"guest-{tokenBase}";
            var suffix = 2;
            while ((reservedTokens?.Contains(token) ?? false) || _db.Guests.Any(item => item.PublicToken == token))
            {
                token = $"guest-{tokenBase}-{suffix++}";
            }

            reservedTokens?.Add(token);
            return token;
        }

        private static string BuildDisplayName(string firstName, string lastName, string? displayName)
        {
            return string.IsNullOrWhiteSpace(displayName)
                ? $"{firstName} {lastName}".Trim()
                : displayName.Trim();
        }

        private static AdminGuestImportRowDto BuildImportPreviewRow(
            AdminGuestImportRow row,
            int fallbackRowNumber,
            HashSet<string> existingKeys,
            Dictionary<string, int> seenKeys,
            Dictionary<string, EventTableEntity> tablesByCode,
            Dictionary<string, EventTableEntity> tablesByName,
            Dictionary<Guid, int> assignedCountsByTable,
            HashSet<string> occupiedSeats,
            HashSet<string> seenSeatKeys,
            string seatingMode)
        {
            var firstName = row.FirstName?.Trim() ?? string.Empty;
            var lastName = row.LastName?.Trim() ?? string.Empty;
            var displayName = BuildDisplayName(firstName, lastName, row.DisplayName);
            var notes = row.Notes?.Trim() ?? string.Empty;
            var personCount = NormalizePersonCount(row.PersonCount);
            var tableNumber = row.TableNumber?.Trim() ?? string.Empty;
            var tableName = row.TableName?.Trim() ?? string.Empty;
            var seatNumber = row.SeatNumber?.Trim();
            var errors = new List<string>();
            EventTableEntity? table = null;

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

            if (!string.IsNullOrWhiteSpace(tableNumber) && !tablesByCode.TryGetValue(tableNumber, out table))
            {
                errors.Add($"Table {tableNumber} was not found.");
            }

            if (table is null && !string.IsNullOrWhiteSpace(tableName) && !tablesByName.TryGetValue(tableName, out table))
            {
                errors.Add($"Table {tableName} was not found.");
            }

            if (table is not null)
            {
                tableNumber = table.Code;
                tableName = table.Name;
                assignedCountsByTable.TryGetValue(table.Id, out var assignedCount);

                if (seatingMode == "seat")
                {
                    var normalizedSeat = NormalizeSeatNumber(seatNumber, table.Capacity);
                    if (normalizedSeat.Error is not null)
                    {
                        errors.Add(normalizedSeat.Error);
                    }
                    else
                    {
                        seatNumber = normalizedSeat.SeatNumber;
                        var seatKey = $"{table.Id}:{seatNumber}";
                        if (occupiedSeats.Contains(seatKey) || !seenSeatKeys.Add(seatKey))
                        {
                            errors.Add($"Seat {seatNumber} at table {table.Code} is already assigned.");
                        }
                    }
                }
                else
                {
                    seatNumber = null;
                    if (assignedCount + personCount > table.Capacity)
                    {
                        errors.Add($"Table {table.Code} does not have enough open seats.");
                    }
                }

                assignedCountsByTable[table.Id] = assignedCount + (seatingMode == "seat" ? 1 : personCount);
            }
            else if (!string.IsNullOrWhiteSpace(seatNumber))
            {
                errors.Add("Seat number requires an assigned table.");
            }

            return new AdminGuestImportRowDto(row.RowNumber ?? fallbackRowNumber, firstName, lastName, displayName, notes, personCount, table?.Id, tableNumber, tableName, seatNumber, isDuplicate, errors.ToArray());
        }

        private static AdminGuestImportRow[] ParseGuestImportCsv(string text)
        {
            var rows = ParseCsv(text).Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell))).ToArray();
            if (rows.Length <= 1) return [];

            var headers = rows[0].Select(NormalizeCsvHeader).ToArray();
            var dataRows = rows.Skip(1).ToArray();
            return dataRows
                .Select((row, index) =>
                {
                    string Value(params string[] names)
                    {
                        var headerIndex = Array.FindIndex(headers, header => names.Contains(header));
                        return headerIndex >= 0 && headerIndex < row.Length ? row[headerIndex].Trim() : string.Empty;
                    }

                    return new AdminGuestImportRow(
                        index + 2,
                        Value("firstname", "first", "first_name"),
                        Value("lastname", "last", "last_name", "surname"),
                        Value("displayname", "display", "name", "fullname"),
                        Value("notes", "note", "comment", "comments"),
                        int.TryParse(Value("personcount", "people", "persons", "partysize", "party", "numberofpersons", "numberofperson"), out var personCount) ? personCount : null,
                        Value("tablenumber", "table", "tablecode", "assignedtable", "assignedtablenumber"),
                        Value("tablename", "assignedtablename"),
                        Value("seatnumber", "seat", "assignedseat", "assignedseatnumber"));
                })
                .Where(row => !string.IsNullOrWhiteSpace(row.FirstName) || !string.IsNullOrWhiteSpace(row.LastName) || !string.IsNullOrWhiteSpace(row.DisplayName))
                .ToArray();
        }

        private static string[][] ParseCsv(string text)
        {
            var rows = new List<string[]>();
            var row = new List<string>();
            var cell = new StringBuilder();
            var inQuotes = false;

            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                if (current == '"')
                {
                    if (inQuotes && index + 1 < text.Length && text[index + 1] == '"')
                    {
                        cell.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (current == ',' && !inQuotes)
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    continue;
                }

                if ((current == '\r' || current == '\n') && !inQuotes)
                {
                    if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row.ToArray());
                    row.Clear();
                    continue;
                }

                cell.Append(current);
            }

            row.Add(cell.ToString());
            if (row.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(row.ToArray());
            return rows.ToArray();
        }

        private static string NormalizeCsvHeader(string value)
        {
            return new string(value
                .Trim()
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());
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
                Math.Max(1, guest.PersonCount),
                guest.TableId,
                guest.Table?.Code ?? string.Empty,
                guest.Table?.Name ?? string.Empty,
                guest.SeatNumber,
                guest.Status,
                isDuplicate);
        }

        private static AdminTableDto ToAdminTableDto(EventTableEntity table, string seatingAssignmentMode = "table")
        {
            return new AdminTableDto(
                table.Id,
                table.Name,
                table.Code,
                table.Capacity,
                CountAssignedSeatsOrPeople(table.Guests, seatingAssignmentMode),
                NormalizeTableShape(table.Shape),
                table.Notes ?? string.Empty);
        }

        private (EventTableEntity? Table, string? SeatNumber, string? Error) ValidateGuestAssignment(Guid eventId, Guid? guestId, Guid? tableId, string? seatNumber, GuestStatus status, int personCount)
        {
            if (tableId is null) return (null, null, null);

            var table = _db.EventTables.Include(item => item.Guests).SingleOrDefault(item => item.Id == tableId && item.EventId == eventId);
            if (table is null) return (null, null, "not-found");

            var seatingMode = GetEventSeatingAssignmentMode(eventId);
            if (seatingMode == "seat")
            {
                var normalizedSeat = NormalizeSeatNumber(seatNumber, table.Capacity);
                if (normalizedSeat.Error is not null) return (null, null, normalizedSeat.Error);

                if (CountsTowardSeating(status))
                {
                    var assignedCount = CountSeatedGuests(table.Guests.Where(item => item.Id != guestId));
                    if (assignedCount + 1 > table.Capacity)
                    {
                        return (null, null, $"Table {table.Code} is full.");
                    }

                    var seatTaken = table.Guests.Any(item =>
                        item.Id != guestId &&
                        CountsTowardSeating(item.Status) &&
                        string.Equals(item.SeatNumber, normalizedSeat.SeatNumber, StringComparison.OrdinalIgnoreCase));
                    if (seatTaken)
                    {
                        return (null, null, $"Seat {normalizedSeat.SeatNumber} at table {table.Code} is already assigned.");
                    }
                }

                return (table, normalizedSeat.SeatNumber, null);
            }

            var assignedPeople = CountSeatedPeople(table.Guests.Where(item => item.Id != guestId));
            var requestedPeople = CountsTowardSeating(status) ? Math.Max(1, personCount) : 0;
            if (assignedPeople + requestedPeople > table.Capacity)
            {
                return (null, null, $"Table {table.Code} is full.");
            }

            return (table, null, null);
        }

        private string GetEventSeatingAssignmentMode(Guid eventId)
        {
            var mode = _db.Events
                .AsNoTracking()
                .Where(item => item.Id == eventId)
                .Select(item => item.SeatingAssignmentMode)
                .SingleOrDefault();

            return NormalizeSeatingAssignmentMode(mode);
        }

        private static (string? SeatNumber, string? Error) NormalizeSeatNumber(string? seatNumber, int tableCapacity)
        {
            if (string.IsNullOrWhiteSpace(seatNumber))
            {
                return (null, "Choose a seat number for this table.");
            }

            if (!int.TryParse(seatNumber.Trim(), out var parsedSeat) || parsedSeat < 1 || parsedSeat > tableCapacity)
            {
                return (null, $"Seat number must be between 1 and {tableCapacity}.");
            }

            return (parsedSeat.ToString(CultureInfo.InvariantCulture), null);
        }

        private static int CountAssignedSeatsOrPeople(IEnumerable<GuestEntity> guests, string seatingAssignmentMode)
        {
            return seatingAssignmentMode == "seat" ? CountSeatedGuests(guests) : CountSeatedPeople(guests);
        }

        private static int CountSeatedGuests(IEnumerable<GuestEntity> guests)
        {
            return guests.Count(guest => CountsTowardSeating(guest.Status));
        }

        private static int CountSeatedPeople(IEnumerable<GuestEntity> guests)
        {
            return guests
                .Where(guest => CountsTowardSeating(guest.Status))
                .Sum(guest => Math.Max(1, guest.PersonCount));
        }

        private static int NormalizePersonCount(int? value)
        {
            return Math.Max(1, value ?? 1);
        }

        private static bool CountsTowardSeating(GuestStatus status)
        {
            return status is GuestStatus.Active or GuestStatus.CheckedIn;
        }

        private static string NormalizeSeatingAssignmentMode(string? mode)
        {
            return string.Equals(mode?.Trim(), "seat", StringComparison.OrdinalIgnoreCase) ? "seat" : "table";
        }

        private static string NormalizeTableShape(string? shape)
        {
            return (shape ?? "round").Trim().ToLowerInvariant() switch
            {
                "square" => "square",
                "rectangle" => "rectangle",
                "tear" or "tear-shaped" or "tear shaped" => "tear",
                _ => "round"
            };
        }

        private static string ToFloorShape(string? shape)
        {
            return NormalizeTableShape(shape);
        }

        private static decimal ShapeDefaultWidth(string? shape)
        {
            return NormalizeTableShape(shape) == "rectangle" ? 0.18m : 0.14m;
        }

        private static decimal ShapeDefaultHeight(string? shape)
        {
            return NormalizeTableShape(shape) == "rectangle" ? 0.11m : 0.14m;
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

        private static decimal NormalizeRotation(decimal? value)
        {
            var rotation = value ?? 0m;
            rotation %= 360m;
            return rotation < 0m ? rotation + 360m : rotation;
        }

        private static FloorPlanSeatPositionDto[] ReadSeatLayout(string? seatLayout)
        {
            if (string.IsNullOrWhiteSpace(seatLayout)) return [];

            try
            {
                var positions = JsonSerializer.Deserialize<FloorPlanSeatPositionDto[]>(seatLayout, SeatLayoutJsonOptions);
                return NormalizeSeatLayout(positions);
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static string SerializeSeatLayout(IEnumerable<FloorPlanSeatPositionDto>? seatLayout)
        {
            return JsonSerializer.Serialize(NormalizeSeatLayout(seatLayout), SeatLayoutJsonOptions);
        }

        private static FloorPlanSeatPositionDto[] NormalizeSeatLayout(IEnumerable<FloorPlanSeatPositionDto>? seatLayout)
        {
            if (seatLayout is null) return [];

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var positions = new List<FloorPlanSeatPositionDto>();

            foreach (var position in seatLayout)
            {
                if (positions.Count >= 128) break;

                var seatNumber = position.SeatNumber?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(seatNumber) || !seen.Add(seatNumber)) continue;

                positions.Add(new FloorPlanSeatPositionDto(
                    seatNumber,
                    ClampSeatLayoutPercent(position.X),
                    ClampSeatLayoutPercent(position.Y)));
            }

            return positions.ToArray();
        }

        private static decimal ClampSeatLayoutPercent(decimal value)
        {
            return Math.Clamp(value, -20m, 120m);
        }

        private static (int Page, int PageSize) NormalizePaging(int? page, int? pageSize)
        {
            return (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 25, 1, 100));
        }
    }

    public sealed class AuthOptions
    {
        public string Issuer { get; set; } = "sassoir.local";
        public string Audience { get; set; } = "sassoir.admin";
        public string SigningKey { get; set; } = string.Empty;
        public int AccessTokenMinutes { get; set; } = 120;
        public int RefreshTokenHours { get; set; } = 24;
        public int PasswordResetTokenMinutes { get; set; } = 30;
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
                TokenSigner.CreateRefresh(user, _options),
                user.Email,
                $"{user.FirstName} {user.LastName}".Trim(),
                roles,
                DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes),
                DateTimeOffset.UtcNow.AddHours(_options.RefreshTokenHours));
        }

        public LoginResponse? Refresh(string refreshToken)
        {
            var claims = TokenSigner.Validate(refreshToken, _options, "refresh");
            if (claims is null) return null;

            var user = _db.Users
                .Include(item => item.UserRoles)
                    .ThenInclude(item => item.Role)
                .SingleOrDefault(item => item.Id == claims.UserId && item.Status == "Active");
            if (user is null) return null;

            var roles = user.UserRoles.Select(item => item.Role?.Name).Where(item => item is not null).Cast<string>().ToArray();
            return new LoginResponse(
                TokenSigner.Create(user, roles, _options),
                TokenSigner.CreateRefresh(user, _options),
                user.Email,
                $"{user.FirstName} {user.LastName}".Trim(),
                roles,
                DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes),
                DateTimeOffset.UtcNow.AddHours(_options.RefreshTokenHours));
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

        public string? ChangePassword(HttpRequest request, string currentPassword, string newPassword)
        {
            var claims = ValidateRequest(request);
            if (claims is null) return "unauthorized";
            if (newPassword.Length < 8) return "New password must be at least 8 characters.";

            var user = _db.Users.SingleOrDefault(item => item.Id == claims.UserId && item.Status == "Active");
            if (user is null) return "unauthorized";
            if (!PasswordHasher.Verify(currentPassword, user.PasswordHash)) return "Current password is incorrect.";

            user.PasswordHash = PasswordHasher.Hash(newPassword);
            user.UpdatedAt = DateTimeOffset.UtcNow;
            _db.SaveChanges();
            return null;
        }

        public string? CreatePasswordReset(string email)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var user = _db.Users.SingleOrDefault(item => item.Email.ToLower() == normalizedEmail && item.Status == "Active");
            return user is null ? null : TokenSigner.CreatePasswordReset(user, _options);
        }

        public string? ResetPassword(string resetToken, string newPassword)
        {
            if (newPassword.Length < 8) return "New password must be at least 8 characters.";

            var claims = TokenSigner.Validate(resetToken, _options, "password-reset");
            if (claims is null) return "unauthorized";

            var user = _db.Users.SingleOrDefault(item => item.Id == claims.UserId && item.Status == "Active");
            if (user is null) return "unauthorized";

            user.PasswordHash = PasswordHasher.Hash(newPassword);
            user.UpdatedAt = DateTimeOffset.UtcNow;
            _db.SaveChanges();
            return null;
        }

        private TokenClaims? ValidateRequest(HttpRequest request)
        {
            var authorization = request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;

            return TokenSigner.Validate(authorization["Bearer ".Length..].Trim(), _options, "access");
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
            return Create(user, roles, options, "access", DateTimeOffset.UtcNow.AddMinutes(options.AccessTokenMinutes));
        }

        public static string CreateRefresh(AppUserEntity user, AuthOptions options)
        {
            return Create(user, [], options, "refresh", DateTimeOffset.UtcNow.AddHours(options.RefreshTokenHours));
        }

        public static string CreatePasswordReset(AppUserEntity user, AuthOptions options)
        {
            return Create(user, [], options, "password-reset", DateTimeOffset.UtcNow.AddMinutes(options.PasswordResetTokenMinutes));
        }

        private static string Create(AppUserEntity user, string[] roles, AuthOptions options, string tokenType, DateTimeOffset expiresAt)
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
                ["typ"] = tokenType,
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = expiresAt.ToUnixTimeSeconds()
            };

            var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
            var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
            var unsigned = $"{header}.{body}";
            var signature = Sign(unsigned, options.SigningKey);
            return $"{unsigned}.{signature}";
        }

        public static TokenClaims? Validate(string token, AuthOptions options, string expectedTokenType)
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
            if (root.TryGetProperty("typ", out var tokenType) && tokenType.GetString() != expectedTokenType) return null;
            if (!root.TryGetProperty("typ", out _) && expectedTokenType != "access") return null;
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
        string SeatingAssignmentMode,
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
        string? TableName,
        int? TableCapacity,
        decimal X,
        decimal Y,
        decimal Width,
        decimal Height,
        decimal Rotation,
        string Shape,
        int ZIndex,
        FloorPlanSeatPositionDto[] SeatLayout);

    public sealed record FloorPlanSeatPositionDto(string SeatNumber, decimal X, decimal Y);

    public sealed record LoginRequest(string Email, string Password);

    public sealed record RefreshTokenRequest(string RefreshToken);

    public sealed record LoginResponse(string Token, string RefreshToken, string Email, string DisplayName, string[] Roles, DateTimeOffset ExpiresAt, DateTimeOffset RefreshExpiresAt);

    public sealed record CurrentUserDto(string Email, string DisplayName, string[] Roles);

    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    public sealed record ForgotPasswordRequest(string Email);

    public sealed record ResetPasswordRequest(string ResetToken, string NewPassword);

    public sealed record UploadResponse(string Url);

    public sealed record AdminGuestDto(
        Guid Id,
        string FirstName,
        string LastName,
        string DisplayName,
        string Notes,
        int PersonCount,
        Guid? TableId,
        string TableCode,
        string TableName,
        string? SeatNumber,
        GuestStatus Status,
        bool IsDuplicate);

    public sealed record AdminGuestCreateRequest(string? FirstName, string? LastName, string? DisplayName, string? Notes, int? PersonCount, Guid? TableId, string? SeatNumber, GuestStatus? Status);

    public sealed record AdminGuestUpsertRequest(string? FirstName, string? LastName, string? DisplayName, string? Notes, int? PersonCount, Guid? TableId, string? SeatNumber, GuestStatus Status);

    public sealed record AdminGuestImportRequest(IReadOnlyList<AdminGuestImportRow> Guests);

    public sealed record AdminGuestImportRow(int? RowNumber, string? FirstName, string? LastName, string? DisplayName, string? Notes, int? PersonCount, string? TableNumber, string? TableName, string? SeatNumber);

    public sealed record AdminGuestImportPreviewDto(AdminGuestImportRowDto[] Rows, int ErrorCount, int DuplicateCount);

    public sealed record AdminGuestImportRowDto(int RowNumber, string FirstName, string LastName, string DisplayName, string Notes, int PersonCount, Guid? TableId, string TableNumber, string TableName, string? SeatNumber, bool IsDuplicate, string[] Errors);

    public sealed record AssignGuestTableRequest(Guid? TableId, string? SeatNumber);

    public sealed record BulkAssignGuestTableRequest(IReadOnlyList<Guid> GuestIds, Guid? TableId);

    public sealed record BulkGuestRequest(IReadOnlyList<Guid> GuestIds);

    public sealed record AdminTableDto(Guid Id, string Name, string Number, int MaximumCapacity, int AssignedGuestCount, string Shape, string Notes);

    public sealed record AdminTableCreateRequest(string Name, string Number, int MaximumCapacity, string? Shape, string? Notes);

    public sealed record AdminTableUpsertRequest(string Name, string Number, int MaximumCapacity, string? Shape, string? Notes, decimal? Width, decimal? Height);

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
        decimal? Rotation,
        string Shape,
        int ZIndex,
        FloorPlanSeatPositionDto[]? SeatLayout);

    public sealed record GuestSearchRequest(string Query);

    public sealed record GuestSearchResponse(IReadOnlyCollection<GuestSearchResultDto> Results);

    public sealed record GuestSearchResultDto(string PublicToken, string DisplayName, string GroupLabel, string Notes);

    public sealed record AdminEventUpsertRequest(
        string Name,
        string Slug,
        string? Subtitle,
        string? DateLabel,
        string? VenueName,
        string? VenueAddress,
        string? EventType,
        string? SeatingAssignmentMode,
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
        string SeatingAssignmentMode,
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
        string SeatingAssignmentMode,
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

    public sealed record PublicSeatResultDto(
        string PublicToken,
        string DisplayName,
        string GroupLabel,
        string TableCode,
        string TableName,
        string? SeatNumber,
        string Directions,
        string[] Companions,
        PublicEventDto Event,
        FloorPlanDto? FloorPlan,
        string? HighlightedObjectId);

    public sealed record GuestFloorPlanDto(FloorPlanDto FloorPlan, string HighlightedObjectId);

    public sealed record GuestMessageRequest(string Message);

    public sealed record AdminGuestMessageDto(Guid Id, string GuestName, string Message, DateTimeOffset CreatedAt);

    public sealed record ContactSubmissionRequest(string Name, string Email, string Message);

    public sealed record ContactSubmissionDto(Guid Id, string Name, string Email, string Message, DateTimeOffset SubmittedAtUtc);

    public sealed record PaginatedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

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
                eventDetails.SeatingAssignmentMode,
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
                eventDetails.SeatingAssignmentMode,
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
                eventDetails.SeatingAssignmentMode == "seat"
                    ? eventDetails.Guests.Count(guest => guest.Status != GuestStatus.Archived && !string.IsNullOrWhiteSpace(guest.TableCode) && !string.IsNullOrWhiteSpace(guest.SeatNumber))
                    : eventDetails.Guests.Count(guest => guest.Status != GuestStatus.Archived && !string.IsNullOrWhiteSpace(guest.TableCode)));
        }

        public static GuestSearchResultDto ToSearchDto(this Guest guest)
        {
            return new GuestSearchResultDto(guest.PublicToken, guest.DisplayName, guest.GroupLabel, string.Empty);
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
