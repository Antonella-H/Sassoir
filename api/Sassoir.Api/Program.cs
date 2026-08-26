using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Sassoir.Api.Data;
using Sassoir.Api.Endpoints;
using Sassoir.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var allowedOrigins = ApiConfiguration.GetAllowedOrigins(builder.Configuration);

builder.Services.AddDbContext<SassoirDbContext>(options =>
{
    options.UseNpgsql(ApiConfiguration.NormalizePostgresConnectionString(builder.Configuration.GetConnectionString("DefaultConnection"), builder.Configuration));
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
    options.AddPolicy("PublicEvent", context => ApiConfiguration.FixedWindowPolicy(context, "RateLimiting:PublicEventPerMinute", 60));
    options.AddPolicy("PublicSearch", context => ApiConfiguration.FixedWindowPolicy(context, "RateLimiting:GuestSearchPerMinute", 30));
    options.AddPolicy("PublicSeat", context => ApiConfiguration.FixedWindowPolicy(context, "RateLimiting:SeatResultPerMinute", 30));
    options.AddPolicy("PublicMessage", context => ApiConfiguration.FixedWindowPolicy(context, "RateLimiting:GuestMessagePerMinute", 5));
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

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapUploadEndpoints();
app.MapPublicEventEndpoints();
app.MapAdminEventEndpoints();
app.MapContactEndpoints();

app.Run();
