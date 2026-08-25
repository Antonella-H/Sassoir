using Microsoft.EntityFrameworkCore;
using Sassoir.Api.Models;

namespace Sassoir.Api.Data;

public sealed class SassoirDbContext(DbContextOptions<SassoirDbContext> options) : DbContext(options)
{
    public DbSet<OrganizationEntity> Organizations => Set<OrganizationEntity>();
    public DbSet<EventEntity> Events => Set<EventEntity>();
    public DbSet<EventThemeEntity> EventThemes => Set<EventThemeEntity>();
    public DbSet<EventTableEntity> EventTables => Set<EventTableEntity>();
    public DbSet<GuestEntity> Guests => Set<GuestEntity>();
    public DbSet<GuestSearchAliasEntity> GuestSearchAliases => Set<GuestSearchAliasEntity>();
    public DbSet<FloorPlanEntity> FloorPlans => Set<FloorPlanEntity>();
    public DbSet<FloorPlanObjectEntity> FloorPlanObjects => Set<FloorPlanObjectEntity>();
    public DbSet<GuestMessageEntity> GuestMessages => Set<GuestMessageEntity>();
    public DbSet<SearchMetricEntity> SearchMetrics => Set<SearchMetricEntity>();
    public DbSet<ContactSubmissionEntity> ContactSubmissions => Set<ContactSubmissionEntity>();
    public DbSet<AppUserEntity> Users => Set<AppUserEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<UserRoleEntity> UserRoles => Set<UserRoleEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrganizationEntity>(entity =>
        {
            entity.ToTable("organizations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.Name).HasColumnName("name");
            entity.Property(item => item.Slug).HasColumnName("slug");
            entity.Property(item => item.Status).HasColumnName("status");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<EventEntity>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.OrganizationId).HasColumnName("organization_id");
            entity.Property(item => item.Name).HasColumnName("name");
            entity.Property(item => item.Slug).HasColumnName("slug");
            entity.Property(item => item.EventType).HasColumnName("event_type");
            entity.Property(item => item.Subtitle).HasColumnName("subtitle");
            entity.Property(item => item.Description).HasColumnName("description");
            entity.Property(item => item.DateLabel).HasColumnName("date_label");
            entity.Property(item => item.VenueName).HasColumnName("venue_name");
            entity.Property(item => item.VenueAddress).HasColumnName("venue_address");
            entity.Property(item => item.SeatingAssignmentMode).HasColumnName("seating_assignment_mode");
            entity.Property(item => item.Status).HasColumnName("status").HasConversion<string>();
            entity.Property(item => item.IsPublic).HasColumnName("is_public");
            entity.Property(item => item.PublishedAt).HasColumnName("published_at");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(item => item.Organization).WithMany(item => item.Events).HasForeignKey(item => item.OrganizationId);
            entity.HasOne(item => item.Theme).WithOne(item => item.Event).HasForeignKey<EventThemeEntity>(item => item.EventId);
            entity.HasMany(item => item.Guests).WithOne(item => item.Event).HasForeignKey(item => item.EventId);
            entity.HasMany(item => item.Tables).WithOne(item => item.Event).HasForeignKey(item => item.EventId);
            entity.HasMany(item => item.FloorPlans).WithOne(item => item.Event).HasForeignKey(item => item.EventId);
        });

        modelBuilder.Entity<EventThemeEntity>(entity =>
        {
            entity.ToTable("event_themes");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.EventId).HasColumnName("event_id");
            entity.Property(item => item.LogoText).HasColumnName("logo_text");
            entity.Property(item => item.HeroText).HasColumnName("hero_text");
            entity.Property(item => item.PrimaryColor).HasColumnName("primary_color");
            entity.Property(item => item.SecondaryColor).HasColumnName("secondary_color");
            entity.Property(item => item.BackgroundColor).HasColumnName("background_color");
            entity.Property(item => item.TextColor).HasColumnName("text_color");
            entity.Property(item => item.WelcomeTitle).HasColumnName("welcome_title");
            entity.Property(item => item.SearchInputLabel).HasColumnName("search_input_label");
            entity.Property(item => item.SearchPlaceholder).HasColumnName("search_placeholder");
            entity.Property(item => item.HeroImageUrl).HasColumnName("hero_image_url");
            entity.Property(item => item.LogoUrl).HasColumnName("logo_url");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<EventTableEntity>(entity =>
        {
            entity.ToTable("event_tables");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.EventId).HasColumnName("event_id");
            entity.Property(item => item.Name).HasColumnName("name");
            entity.Property(item => item.Code).HasColumnName("code");
            entity.Property(item => item.Shape).HasColumnName("shape");
            entity.Property(item => item.Capacity).HasColumnName("capacity");
            entity.Property(item => item.Notes).HasColumnName("notes");
            entity.Property(item => item.ZoneName).HasColumnName("zone_name");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<GuestEntity>(entity =>
        {
            entity.ToTable("guests");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.EventId).HasColumnName("event_id");
            entity.Property(item => item.GuestGroupId).HasColumnName("guest_group_id");
            entity.Property(item => item.TableId).HasColumnName("table_id");
            entity.Property(item => item.FirstName).HasColumnName("first_name");
            entity.Property(item => item.LastName).HasColumnName("last_name");
            entity.Property(item => item.DisplayName).HasColumnName("display_name");
            entity.Property(item => item.NormalizedSearchName).HasColumnName("normalized_search_name");
            entity.Property(item => item.PublicToken).HasColumnName("public_token");
            entity.Property(item => item.GroupLabel).HasColumnName("group_label");
            entity.Property(item => item.SeatNumber).HasColumnName("seat_number");
            entity.Property(item => item.Directions).HasColumnName("directions");
            entity.Property(item => item.Email).HasColumnName("email");
            entity.Property(item => item.Phone).HasColumnName("phone");
            entity.Property(item => item.Notes).HasColumnName("notes");
            entity.Property(item => item.PersonCount).HasColumnName("person_count");
            entity.Property(item => item.Status).HasColumnName("status").HasConversion<string>();
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(item => item.Table).WithMany(item => item.Guests).HasForeignKey(item => item.TableId);
            entity.HasMany(item => item.SearchAliases).WithOne(item => item.Guest).HasForeignKey(item => item.GuestId);
        });

        modelBuilder.Entity<GuestSearchAliasEntity>(entity =>
        {
            entity.ToTable("guest_search_aliases");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.GuestId).HasColumnName("guest_id");
            entity.Property(item => item.Alias).HasColumnName("alias");
            entity.Property(item => item.NormalizedAlias).HasColumnName("normalized_alias");
        });

        modelBuilder.Entity<FloorPlanEntity>(entity =>
        {
            entity.ToTable("floor_plans");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.EventId).HasColumnName("event_id");
            entity.Property(item => item.Name).HasColumnName("name");
            entity.Property(item => item.CanvasAspectRatio).HasColumnName("canvas_aspect_ratio");
            entity.Property(item => item.Version).HasColumnName("version");
            entity.Property(item => item.IsActive).HasColumnName("is_active");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.HasMany(item => item.Objects).WithOne(item => item.FloorPlan).HasForeignKey(item => item.FloorPlanId);
        });

        modelBuilder.Entity<FloorPlanObjectEntity>(entity =>
        {
            entity.ToTable("floor_plan_objects");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.FloorPlanId).HasColumnName("floor_plan_id");
            entity.Property(item => item.LinkedTableId).HasColumnName("linked_table_id");
            entity.Property(item => item.ObjectType).HasColumnName("object_type");
            entity.Property(item => item.Label).HasColumnName("label");
            entity.Property(item => item.X).HasColumnName("x");
            entity.Property(item => item.Y).HasColumnName("y");
            entity.Property(item => item.Width).HasColumnName("width");
            entity.Property(item => item.Height).HasColumnName("height");
            entity.Property(item => item.Rotation).HasColumnName("rotation");
            entity.Property(item => item.Shape).HasColumnName("shape");
            entity.Property(item => item.ZIndex).HasColumnName("z_index");
            entity.Property(item => item.SeatLayout).HasColumnName("seat_layout");
            entity.Property(item => item.IsVisible).HasColumnName("is_visible");
        });

        modelBuilder.Entity<GuestMessageEntity>(entity =>
        {
            entity.ToTable("guest_messages");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.EventId).HasColumnName("event_id");
            entity.Property(item => item.GuestId).HasColumnName("guest_id");
            entity.Property(item => item.Message).HasColumnName("message");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.HasOne(item => item.Guest).WithMany().HasForeignKey(item => item.GuestId);
        });

        modelBuilder.Entity<SearchMetricEntity>(entity =>
        {
            entity.ToTable("search_metrics");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.EventId).HasColumnName("event_id");
            entity.Property(item => item.NormalizedQuery).HasColumnName("normalized_query");
            entity.Property(item => item.Successful).HasColumnName("successful");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<ContactSubmissionEntity>(entity =>
        {
            entity.ToTable("contact_submissions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.Name).HasColumnName("name");
            entity.Property(item => item.Email).HasColumnName("email");
            entity.Property(item => item.Message).HasColumnName("message");
            entity.Property(item => item.SubmittedAtUtc).HasColumnName("submitted_at_utc").HasDefaultValueSql("now()");
            entity.HasIndex(item => item.SubmittedAtUtc).HasDatabaseName("ix_contact_submissions_submitted_at_utc");
        });

        modelBuilder.Entity<AppUserEntity>(entity =>
        {
            entity.ToTable("app_users");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.OrganizationId).HasColumnName("organization_id");
            entity.Property(item => item.FirstName).HasColumnName("first_name");
            entity.Property(item => item.LastName).HasColumnName("last_name");
            entity.Property(item => item.Email).HasColumnName("email");
            entity.Property(item => item.PasswordHash).HasColumnName("password_hash");
            entity.Property(item => item.Status).HasColumnName("status");
            entity.Property(item => item.IsSuperAdmin).HasColumnName("is_super_admin");
            entity.Property(item => item.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            entity.HasMany(item => item.UserRoles).WithOne(item => item.User).HasForeignKey(item => item.UserId);
        });

        modelBuilder.Entity<RoleEntity>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.Name).HasColumnName("name");
            entity.HasMany(item => item.UserRoles).WithOne(item => item.Role).HasForeignKey(item => item.RoleId);
        });

        modelBuilder.Entity<UserRoleEntity>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(item => new { item.UserId, item.RoleId });
            entity.Property(item => item.UserId).HasColumnName("user_id");
            entity.Property(item => item.RoleId).HasColumnName("role_id");
        });
    }
}

public sealed class OrganizationEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<EventEntity> Events { get; set; } = [];
}

public sealed class EventEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string EventType { get; set; } = "Wedding";
    public string Subtitle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DateLabel { get; set; } = string.Empty;
    public string VenueName { get; set; } = string.Empty;
    public string VenueAddress { get; set; } = string.Empty;
    public string SeatingAssignmentMode { get; set; } = "table";
    public EventStatus Status { get; set; }
    public bool IsPublic { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public OrganizationEntity? Organization { get; set; }
    public EventThemeEntity? Theme { get; set; }
    public List<EventTableEntity> Tables { get; set; } = [];
    public List<GuestEntity> Guests { get; set; } = [];
    public List<FloorPlanEntity> FloorPlans { get; set; } = [];
}

public sealed class EventThemeEntity
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string LogoText { get; set; } = string.Empty;
    public string HeroText { get; set; } = string.Empty;
    public string PrimaryColor { get; set; } = "#D8CFBC";
    public string SecondaryColor { get; set; } = "#565449";
    public string BackgroundColor { get; set; } = "#FFFBF4";
    public string TextColor { get; set; } = "#11120D";
    public string WelcomeTitle { get; set; } = string.Empty;
    public string SearchInputLabel { get; set; } = "Search by name";
    public string SearchPlaceholder { get; set; } = "Search by name";
    public string? HeroImageUrl { get; set; }
    public string? LogoUrl { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EventEntity? Event { get; set; }
}

public sealed class EventTableEntity
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Shape { get; set; } = "Round";
    public int Capacity { get; set; }
    public string? Notes { get; set; }
    public string? ZoneName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EventEntity? Event { get; set; }
    public List<GuestEntity> Guests { get; set; } = [];
}

public sealed class GuestEntity
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid? GuestGroupId { get; set; }
    public Guid? TableId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedSearchName { get; set; } = string.Empty;
    public string PublicToken { get; set; } = string.Empty;
    public string GroupLabel { get; set; } = string.Empty;
    public string? SeatNumber { get; set; }
    public string Directions { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
    public int PersonCount { get; set; } = 1;
    public GuestStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EventEntity? Event { get; set; }
    public EventTableEntity? Table { get; set; }
    public List<GuestSearchAliasEntity> SearchAliases { get; set; } = [];
}

public sealed class GuestSearchAliasEntity
{
    public Guid Id { get; set; }
    public Guid GuestId { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string NormalizedAlias { get; set; } = string.Empty;
    public GuestEntity? Guest { get; set; }
}

public sealed class FloorPlanEntity
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal CanvasAspectRatio { get; set; }
    public int Version { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public EventEntity? Event { get; set; }
    public List<FloorPlanObjectEntity> Objects { get; set; } = [];
}

public sealed class FloorPlanObjectEntity
{
    public string Id { get; set; } = string.Empty;
    public Guid FloorPlanId { get; set; }
    public Guid? LinkedTableId { get; set; }
    public string ObjectType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public decimal Rotation { get; set; }
    public string Shape { get; set; } = "rect";
    public int ZIndex { get; set; }
    public string SeatLayout { get; set; } = "[]";
    public bool IsVisible { get; set; } = true;
    public FloorPlanEntity? FloorPlan { get; set; }
}

public sealed class GuestMessageEntity
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid GuestId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public GuestEntity? Guest { get; set; }
}

public sealed class SearchMetricEntity
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string NormalizedQuery { get; set; } = string.Empty;
    public bool Successful { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ContactSubmissionEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset SubmittedAtUtc { get; set; }
}

public sealed class AppUserEntity
{
    public Guid Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public bool IsSuperAdmin { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<UserRoleEntity> UserRoles { get; set; } = [];
}

public sealed class RoleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<UserRoleEntity> UserRoles { get; set; } = [];
}

public sealed class UserRoleEntity
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public AppUserEntity? User { get; set; }
    public RoleEntity? Role { get; set; }
}
