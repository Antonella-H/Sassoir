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
        string DjAccessToken,
        string EventType,
        string SeatingAssignmentMode,
        string Subtitle,
        string DateLabel,
        string VenueName,
        string VenueAddress,
        bool EnableFloorPlan,
        bool EnableTableCompanions,
        bool EnableGuestMessages,
        bool EnableSongRequests,
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
        bool? EnableFloorPlan,
        bool? EnableTableCompanions,
        bool? EnableGuestMessages,
        bool? EnableSongRequests,
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
        string DjAccessToken,
        string EventType,
        string SeatingAssignmentMode,
        string Subtitle,
        string DateLabel,
        string VenueName,
        string VenueAddress,
        bool EnableFloorPlan,
        bool EnableTableCompanions,
        bool EnableGuestMessages,
        bool EnableSongRequests,
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
        bool EnableFloorPlan,
        bool EnableTableCompanions,
        bool EnableGuestMessages,
        bool EnableSongRequests,
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

    public sealed record SongRequestCreateRequest(string SongTitle);

    public sealed record PublicSongRequestDto(Guid Id, string SongTitle, DateTimeOffset CreatedAt);

    public sealed record AdminSongRequestDto(Guid Id, string GuestName, string SongTitle, DateTimeOffset CreatedAt);

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
                eventDetails.EnableFloorPlan,
                eventDetails.EnableTableCompanions,
                eventDetails.EnableGuestMessages,
                eventDetails.EnableSongRequests,
                eventDetails.Theme);
        }

        public static AdminEventDto ToAdminDto(this EventDetails eventDetails)
        {
            return new AdminEventDto(
                eventDetails.Id,
                eventDetails.Name,
                eventDetails.Slug,
                eventDetails.DjAccessToken,
                eventDetails.EventType,
                eventDetails.SeatingAssignmentMode,
                eventDetails.Subtitle,
                eventDetails.DateLabel,
                eventDetails.VenueName,
                eventDetails.VenueAddress,
                eventDetails.EnableFloorPlan,
                eventDetails.EnableTableCompanions,
                eventDetails.EnableGuestMessages,
                eventDetails.EnableSongRequests,
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
