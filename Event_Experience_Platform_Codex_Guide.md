# Event Experience & Seating Platform — Codex Implementation Guide

## 1. Project Overview

Build a lightweight, mobile-first event experience platform that initially focuses on helping guests find their assigned seat or table after scanning a QR code.

The public guest experience should be extremely simple:

1. Scan the event QR code.
2. Open the event welcome page.
3. Search for the guest name.
4. View the assigned table or seat.
5. View a mobile-friendly floor plan showing where the table is located.

The administrative side should allow platform administrators, event planners, and event hosts to create events, upload guest lists, assign seats or tables, design venue floor plans, publish event pages, and generate QR codes.

The platform must be built as a reusable multi-event product rather than a website created for one specific event.

---

## 2. Product Vision

The platform should make event arrival smoother, more elegant, and more interactive.

It should support weddings, corporate events, conferences, galas, dinners, award ceremonies, private parties, exhibitions, and other seated or organized events.

The initial release should remain focused and affordable while leaving room for future features such as:

- Event galleries
- Event schedules
- Guest announcements
- Digital invitations
- RSVP management
- Menu display
- Sponsor sections
- Live event updates
- Guest messaging
- Event analytics
- Photo sharing
- Personalized guest experiences

---

## 3. Main User Types

### 3.1 Super Admin

The Super Admin manages the entire platform.

Permissions:

- View and manage all organizations
- View and manage all users
- Create, edit, archive, or delete events
- Manage subscriptions or plans in future versions
- View platform-wide analytics
- Manage system settings
- Access all event data
- Impersonate or assist organization admins if implemented
- Manage reusable templates
- Manage global lookup values

### 3.2 Organization Admin / Admin

An Admin manages events belonging to their organization.

Permissions:

- Create and manage events
- Invite event planners and hosts
- Upload guest lists
- Manage seating assignments
- Create and edit floor plans
- Customize event branding
- Publish and unpublish events
- Generate and download event QR codes
- View event analytics
- Export guest and seating data

### 3.3 Event Planner

An Event Planner works on one or more assigned events.

Permissions:

- View assigned events
- Edit event details
- Upload or update guest lists
- Assign tables and seats
- Design floor plans
- Preview event pages
- Generate event QR codes
- Publish changes when granted permission

### 3.4 Host

A Host represents the event owner or organizer.

Permissions should be configurable.

Typical permissions:

- View event details
- Review the guest list
- Review seating assignments
- Review the floor plan
- Approve event content
- View analytics
- Make limited edits when permitted

### 3.5 Public Guest / End User

The guest does not need an account.

Capabilities:

- Open the event using a QR code or event URL
- View the welcome page
- Search by name
- Select the correct matching guest
- View assigned table and optional seat
- View the table location on the floor plan
- View optional event details, gallery, schedule, menu, or announcements

---

## 4. Core Guest Flow

### 4.1 QR Entry

Each published event has:

- A unique public slug
- A public event URL
- A downloadable QR code
- Optional QR variants for print use

Example:

```text
https://platform-domain.com/e/roula-and-licha
```

### 4.2 Welcome Page

The welcome page should display:

- Event title
- Optional subtitle
- Host names
- Event date
- Venue name
- Background image or hero image
- Event logo or monogram
- Short welcome message
- Guest search input
- Search button
- Optional event gallery preview
- Optional event information button

Example copy:

```text
Welcome to licha & Roula's Wedding
Find your name below to discover your table.
```

### 4.3 Guest Search

The guest enters a full or partial name.

Search behavior:

- Case-insensitive
- Accent-insensitive where practical
- Supports partial matches
- Handles Arabic and Latin names
- Trims extra spaces
- Should not expose the entire guest list before a search
- Should limit returned results
- Should support duplicate names
- Should display enough information to distinguish duplicates without exposing sensitive data

Possible duplicate-name indicators:

- Guest group or family name
- Last-name initial
- Companion name
- Invitation group
- Optional host-defined label

### 4.4 Seat Result Page

After selecting a guest, display:

- Guest name
- Event title
- Assigned table name or number
- Assigned seat number if used
- Guest group if relevant
- A clear success message
- Button to view the floor plan
- Highlighted table preview
- Optional directions or venue zone
- Button to return to search

Example:

```text
Welcome, Antonella!
Your table is Table 12 — Olive Garden.
```

### 4.5 Mobile Floor Plan

The floor plan should be optimized for mobile devices.

Requirements:

- Highlight the guest's assigned table
- Dim or reduce emphasis on other tables
- Support zoom and pan
- Fit the venue plan within the screen
- Display table labels
- Display venue zones when configured
- Show a legend
- Provide a “Center on my table” action
- Provide a “Back to seat details” action
- Avoid requiring horizontal page scrolling
- Load quickly on mobile networks

---

## 5. Administrative Flow

### 5.1 Dashboard

The dashboard should show:

- Total events
- Upcoming events
- Draft events
- Published events
- Total guests
- Assigned guests
- Unassigned guests
- Recent activity
- Quick-create event action
- Recent events list

### 5.2 Event Creation Wizard

Recommended steps:

1. Basic Information
2. Branding and Theme
3. Venue Information
4. Guest List
5. Tables and Seating
6. Floor Plan
7. Public Page Preview
8. Publish and QR Code

The wizard should save progress as a draft.

### 5.3 Guest List Management

Admins should be able to:

- Add guests manually
- Edit guests
- Delete or archive guests
- Import guests using Excel or CSV
- Export guests
- Search and filter guests
- Assign guests to groups
- Assign tables
- Assign seats
- Bulk assign guests
- Move guests between tables
- Mark guests as unassigned
- Detect duplicate records
- View import errors before saving

Recommended import columns:

```text
FirstName
LastName
DisplayName
GuestGroup
TableName
SeatNumber
CompanionName
Phone
Email
Notes
SearchAliases
```

Only the necessary fields should be required.

### 5.4 Table Management

Admins should be able to:

- Create tables
- Name or number tables
- Select table shape
- Set capacity
- Assign a venue zone
- Add optional table description
- View occupied and available capacity
- Reorder table labels
- Duplicate tables
- Remove tables when no guests are assigned
- Prevent accidental deletion of assigned tables

Supported table shapes for MVP:

- Round
- Rectangle
- Square

Optional future shapes:

- Oval
- Long banquet
- Custom shape

### 5.5 Floor Plan Designer

The floor plan designer should allow users to visually create the venue layout.

MVP capabilities:

- Create a blank floor plan
- Upload a venue background image
- Add tables to the canvas
- Drag tables
- Resize tables
- Rotate tables
- Edit table labels
- Add basic objects
- Save positions
- Preview mobile display
- Highlight a selected table
- Undo and redo
- Zoom in and out
- Snap to grid optionally

Basic floor plan objects:

- Tables
- Stage
- Dance floor
- Entrance
- Exit
- Bar
- Buffet
- Restroom
- DJ area
- Reception
- Decorative label or zone

Each floor-plan object should store normalized coordinates so the layout remains responsive across screen sizes.

Recommended normalized values:

```text
x: 0.0 to 1.0
y: 0.0 to 1.0
width: 0.0 to 1.0
height: 0.0 to 1.0
rotation: degrees
```

Do not store only fixed pixel positions.

### 5.6 Event Branding

Each event should support:

- Primary color
- Secondary color
- Accent color
- Text color
- Background color
- Hero image
- Event logo
- Monogram
- Font selection from an approved list
- Button style
- Card style
- Optional background pattern
- Optional custom welcome message

The default experience should remain elegant, clean, and easy to read.

Themes should work especially well for:

- Earthy wedding themes
- Minimal white themes
- Dark luxury themes
- Corporate themes
- Colorful celebration themes

---

## 6. Required Pages

### 6.1 Public Pages

- Event Welcome Page
- Guest Search Results
- Seat Result Page
- Mobile Floor Plan Page
- Event Not Found Page
- Event Not Published Page
- Optional Event Information Page
- Optional Gallery Page
- Optional Schedule Page

### 6.2 Authentication Pages

- Sign In
- Forgot Password
- Reset Password
- Accept Invitation
- Optional SSO callback page

### 6.3 Admin Pages

- Dashboard
- Events List
- Create Event
- Edit Event
- Event Overview
- Guest List
- Guest Import
- Tables and Seating
- Floor Plan Designer
- Event Branding
- Public Page Preview
- QR Code and Sharing
- Team and Permissions
- Event Analytics
- Organization Settings
- User Profile
- Super Admin Platform Dashboard
- Super Admin Organizations
- Super Admin Users
- Super Admin Events

---

## 7. Recommended Technology Stack

Build the solution using a cost-conscious architecture that can scale later.

### 7.1 Frontend

Recommended:

- React
- TypeScript
- Vite
- React Router
- TanStack Query
- React Hook Form
- Zod
- Tailwind CSS
- shadcn/ui or a similarly lightweight component library
- Konva.js or Fabric.js for the floor plan designer

Alternative:

- Next.js may be used if server-side rendering, metadata management, or a unified deployment model is preferred.

For the lowest-complexity MVP, use React with Vite.

### 7.2 Backend

Recommended:

- ASP.NET Core Web API
- .NET 8 or later supported LTS version
- Entity Framework Core
- FluentValidation
- JWT authentication
- ASP.NET Core Identity or a lightweight user implementation
- Clean Architecture principles without unnecessary overengineering

Suggested projects:

```text
src/
  EventPlatform.Api
  EventPlatform.Application
  EventPlatform.Domain
  EventPlatform.Infrastructure
tests/
  EventPlatform.UnitTests
  EventPlatform.IntegrationTests
```

A modular monolith is preferred for the MVP.

Do not create microservices for the initial version.

### 7.3 Database

Recommended:

- PostgreSQL

Reasons:

- Low-cost hosting options
- Strong relational support
- Good JSON support
- Reliable indexing
- Works well with .NET and EF Core

Alternative:

- SQL Server may be used when alignment with an existing Microsoft environment is more important than minimizing hosting cost.

### 7.4 File Storage

Use object storage for:

- Event images
- Logos
- Floor plan backgrounds
- Gallery photos
- Import files
- Export files

Recommended options:

- Cloudflare R2
- Azure Blob Storage
- Supabase Storage
- S3-compatible object storage

Do not store large files directly in the relational database.

### 7.5 Hosting

Cost-conscious MVP options:

- Frontend: Cloudflare Pages, Vercel, Netlify, or Azure Static Web Apps
- API: Azure App Service, Azure Container Apps, Render, Railway, or Fly.io
- Database: Azure Database for PostgreSQL, Neon, Supabase, Railway, or Render
- Storage: Cloudflare R2 or Azure Blob Storage
- DNS and CDN: Cloudflare

Prefer the existing team’s Azure experience when operational simplicity matters.

---

## 8. Suggested Solution Architecture

```text
Guest Mobile Browser
        |
        v
React Public Web Application
        |
        v
ASP.NET Core API
        |
        +--> PostgreSQL
        |
        +--> Object Storage
        |
        +--> QR Code Generator
        |
        +--> Email Provider
```

The admin portal and public event experience may initially exist in one React application with separate layouts and routes.

Example route structure:

```text
/public
  /e/:eventSlug
  /e/:eventSlug/search
  /e/:eventSlug/guest/:guestToken
  /e/:eventSlug/floor-plan

/admin
  /dashboard
  /events
  /events/:eventId
  /events/:eventId/guests
  /events/:eventId/seating
  /events/:eventId/floor-plan
  /events/:eventId/branding
  /events/:eventId/publish

/super-admin
  /organizations
  /users
  /events
```

---

## 9. Multi-Tenancy

The system should support multiple organizations.

Every organization-owned entity must include an `OrganizationId`.

Examples:

- Events
- Users
- Event members
- Guests
- Tables
- Floor plans
- Media
- Imports
- Audit records

Rules:

- Organization users must only access their organization’s data.
- Event planners and hosts must only access assigned events.
- Super Admins may access all organizations.
- Every relevant API query must apply tenant filtering.
- Never rely only on frontend authorization.
- Validate organization and event access on the backend.

---

## 10. Proposed Data Model

### 10.1 Organization

```text
Id
Name
Slug
LogoUrl
Status
CreatedAt
UpdatedAt
```

### 10.2 User

```text
Id
OrganizationId nullable for Super Admin
FirstName
LastName
Email
PasswordHash
Status
IsSuperAdmin
LastLoginAt
CreatedAt
UpdatedAt
```

### 10.3 Role

```text
Id
Name
```

Initial values:

```text
SuperAdmin
Admin
EventPlanner
Host
```

### 10.4 UserRole

```text
UserId
RoleId
```

### 10.5 Event

```text
Id
OrganizationId
Name
Slug
EventType
Description
WelcomeMessage
StartDateTime
EndDateTime
Timezone
VenueName
VenueAddress
Status
IsPublic
PublishedAt
CreatedByUserId
CreatedAt
UpdatedAt
```

Event statuses:

```text
Draft
ReadyForReview
Published
Completed
Archived
```

### 10.6 EventMember

```text
Id
EventId
UserId
Role
CanEdit
CanPublish
CanManageGuests
CanManageFloorPlan
CreatedAt
```

### 10.7 EventTheme

```text
Id
EventId
PrimaryColor
SecondaryColor
AccentColor
TextColor
BackgroundColor
FontFamily
HeroImageUrl
LogoUrl
BackgroundImageUrl
ButtonStyle
CardStyle
CustomCss nullable
UpdatedAt
```

Avoid unrestricted custom CSS during the MVP unless properly sanitized.

### 10.8 Guest

```text
Id
EventId
FirstName
LastName
DisplayName
NormalizedSearchName
GuestGroupId nullable
TableId nullable
SeatNumber nullable
CompanionName nullable
Email nullable
Phone nullable
Notes nullable
PublicToken
Status
CreatedAt
UpdatedAt
```

Guest statuses:

```text
Active
Cancelled
CheckedIn
Archived
```

The `PublicToken` should be random and non-sequential.

Do not expose the internal Guest ID in public URLs.

### 10.9 GuestSearchAlias

```text
Id
GuestId
Alias
NormalizedAlias
```

This supports:

- Alternative spellings
- Arabic names
- Latin transliterations
- Nicknames
- Married names
- Common misspellings

### 10.10 GuestGroup

```text
Id
EventId
Name
Description nullable
```

### 10.11 EventTable

```text
Id
EventId
Name
Code
Shape
Capacity
ZoneId nullable
FloorPlanX
FloorPlanY
FloorPlanWidth
FloorPlanHeight
Rotation
StyleJson nullable
CreatedAt
UpdatedAt
```

### 10.12 VenueZone

```text
Id
EventId
Name
Description nullable
SortOrder
```

### 10.13 FloorPlan

```text
Id
EventId
Name
BackgroundImageUrl nullable
CanvasAspectRatio
WidthReference
HeightReference
Version
IsActive
CreatedAt
UpdatedAt
```

### 10.14 FloorPlanObject

```text
Id
FloorPlanId
ObjectType
LinkedTableId nullable
Label
X
Y
Width
Height
Rotation
ZIndex
StyleJson
IsVisible
IsLocked
CreatedAt
UpdatedAt
```

Object types:

```text
Table
Stage
DanceFloor
Entrance
Exit
Bar
Buffet
Restroom
DJ
Reception
Text
Shape
Image
```

### 10.15 EventMedia

```text
Id
EventId
MediaType
FileUrl
ThumbnailUrl nullable
Title nullable
Caption nullable
SortOrder
IsPublic
CreatedAt
```

### 10.16 GuestImport

```text
Id
EventId
FileName
FileUrl
Status
TotalRows
ValidRows
InvalidRows
ImportedRows
ErrorReportUrl nullable
CreatedByUserId
CreatedAt
CompletedAt nullable
```

### 10.17 AuditLog

```text
Id
OrganizationId
EventId nullable
UserId nullable
Action
EntityType
EntityId
OldValuesJson nullable
NewValuesJson nullable
IpAddress nullable
UserAgent nullable
CreatedAt
```

---

## 11. API Requirements

Use REST APIs with clear resource-based routes.

### 11.1 Public Event APIs

```http
GET /api/public/events/{slug}
GET /api/public/events/{slug}/theme
GET /api/public/events/{slug}/floor-plan
GET /api/public/events/{slug}/media
POST /api/public/events/{slug}/guests/search
GET /api/public/events/{slug}/guests/{publicToken}
GET /api/public/events/{slug}/guests/{publicToken}/floor-plan
```

Guest search request:

```json
{
  "query": "Antonella"
}
```

Guest search response:

```json
{
  "results": [
    {
      "publicToken": "random-public-token",
      "displayName": "Antonella H.",
      "groupLabel": "Hitti Family"
    }
  ]
}
```

Do not return table assignment in the general search result unless intentionally approved.

### 11.2 Authentication APIs

```http
POST /api/auth/login
POST /api/auth/refresh
POST /api/auth/logout
POST /api/auth/forgot-password
POST /api/auth/reset-password
POST /api/auth/accept-invitation
GET  /api/auth/me
```

### 11.3 Event Management APIs

```http
GET    /api/events
POST   /api/events
GET    /api/events/{eventId}
PUT    /api/events/{eventId}
DELETE /api/events/{eventId}
POST   /api/events/{eventId}/publish
POST   /api/events/{eventId}/unpublish
GET    /api/events/{eventId}/preview
GET    /api/events/{eventId}/qr-code
```

### 11.4 Guest APIs

```http
GET    /api/events/{eventId}/guests
POST   /api/events/{eventId}/guests
GET    /api/events/{eventId}/guests/{guestId}
PUT    /api/events/{eventId}/guests/{guestId}
DELETE /api/events/{eventId}/guests/{guestId}
POST   /api/events/{eventId}/guests/import
POST   /api/events/{eventId}/guests/import/validate
GET    /api/events/{eventId}/guests/export
POST   /api/events/{eventId}/guests/bulk-assign
POST   /api/events/{eventId}/guests/bulk-move
```

### 11.5 Table APIs

```http
GET    /api/events/{eventId}/tables
POST   /api/events/{eventId}/tables
PUT    /api/events/{eventId}/tables/{tableId}
DELETE /api/events/{eventId}/tables/{tableId}
POST   /api/events/{eventId}/tables/bulk-create
```

### 11.6 Floor Plan APIs

```http
GET  /api/events/{eventId}/floor-plan
POST /api/events/{eventId}/floor-plan
PUT  /api/events/{eventId}/floor-plan
POST /api/events/{eventId}/floor-plan/objects
PUT  /api/events/{eventId}/floor-plan/objects/{objectId}
DELETE /api/events/{eventId}/floor-plan/objects/{objectId}
POST /api/events/{eventId}/floor-plan/save-layout
POST /api/events/{eventId}/floor-plan/duplicate
```

Prefer one bulk save endpoint for designer interactions to reduce excessive network calls.

---

## 12. Search Rules

Guest search is one of the most important parts of the platform.

Implement:

- Lowercase normalization
- Whitespace normalization
- Accent removal where supported
- Arabic character normalization where useful
- Search aliases
- Starts-with matching
- Contains matching
- Ranking exact matches first
- Limit results
- Minimum query length
- Rate limiting
- Debounce on the frontend

Recommended ranking:

1. Exact display-name match
2. Exact alias match
3. Starts-with display-name match
4. Starts-with alias match
5. Contains display-name match
6. Contains alias match

Do not use public autocomplete that reveals the complete guest list.

---

## 13. Security Requirements

- Use HTTPS only in production.
- Hash passwords using the framework’s secure password hashing.
- Use short-lived access tokens and refresh tokens.
- Store refresh tokens securely.
- Enforce role and event access on the API.
- Add rate limiting to public search.
- Validate all uploaded files.
- Restrict file types.
- Limit upload size.
- Sanitize filenames.
- Prevent spreadsheet formula injection in exports.
- Prevent mass assignment.
- Use parameterized queries through EF Core.
- Do not expose sequential guest IDs publicly.
- Log sensitive admin actions.
- Avoid storing unnecessary guest personal data.
- Never return private fields from public APIs.
- Add CORS restrictions.
- Add security headers.
- Protect public endpoints from scraping where possible.
- Consider CAPTCHA only when suspicious activity is detected.
- Add soft deletion where business recovery is important.

---

## 14. Privacy Rules

The system should minimize exposure of guest information.

Public search should not display:

- Full email
- Full phone number
- Notes
- Internal IDs
- Private invitation details

For duplicate names, display only safe distinguishing information defined by the event organizer.

Examples:

```text
Antonella H. — Hitti Family
Antonella H. — Guest of Roula
```

Provide an event-level privacy setting controlling which public labels may appear.

---

## 15. Validation Rules

### Event

- Name is required.
- Slug is required and unique.
- Event start date is required.
- Venue name may be optional during draft.
- Public publishing requires required fields to be complete.
- Slug should only contain safe URL characters.

### Guest

- Display name is required.
- Event is required.
- Assigned table must belong to the same event.
- Seat number must be unique within the table only when numbered seating is enabled.
- Public token must be unique.

### Table

- Name is required.
- Capacity must be greater than zero.
- Assigned guest count must not exceed capacity unless event settings allow overbooking.
- A table cannot be deleted while guests are assigned unless reassigned or confirmed.

### Floor Plan

- Linked tables must belong to the event.
- Coordinates must remain within supported bounds.
- Object dimensions must be positive.
- Only one active floor plan should be used for the public guest experience in the MVP.

### Publishing

An event cannot be published unless:

- Event name exists
- Event date exists
- Public slug exists
- At least one guest exists
- Guest search is enabled
- At least one table exists when table seating is enabled
- Active floor plan exists when floor-plan display is enabled
- Theme contains valid readable colors

---

## 16. UI/UX Direction

The overall design should feel:

- Elegant
- Warm
- Modern
- Minimal
- Mobile-first
- Fast
- Celebratory without being visually crowded

### Welcome Page

- Full-screen or near-full-screen hero section
- Large event title
- Short welcome message
- Central guest search card
- Clear primary action
- Soft visual hierarchy
- Optional event imagery
- Smooth but subtle animation

### Seat Result Page

- Immediate success confirmation
- Large table identifier
- Visually clear table card
- One main button to open the floor plan
- Minimal distractions
- Easy return to search

### Mobile Floor Plan

- Table highlighted with glow, pulse, border, or contrast
- Sticky header or bottom action bar
- Zoom and reset controls
- Fast rendering
- Large touch targets
- Clear table labels

### Admin Portal

- Clean dashboard
- Left navigation on desktop
- Responsive drawer on mobile
- Table and card views
- Clear event-status badges
- Bulk actions
- Autosave where appropriate
- Confirmation for destructive actions
- Toast notifications
- Empty states with next-step actions

---

## 17. Suggested Frontend Folder Structure

```text
src/
  api/
  app/
  assets/
  components/
    common/
    forms/
    layout/
    floor-plan/
  features/
    auth/
    events/
    guests/
    tables/
    floor-plan/
    branding/
    public-event/
    organizations/
  hooks/
  lib/
  pages/
    public/
    admin/
    super-admin/
  routes/
  schemas/
  stores/
  types/
  utils/
```

Use feature-based organization.

Avoid putting all components in one shared folder.

---

## 18. Suggested Backend Structure

```text
EventPlatform.Domain/
  Common/
  Entities/
  Enums/
  Events/
  ValueObjects/

EventPlatform.Application/
  Abstractions/
  Authentication/
  Events/
  Guests/
  Tables/
  FloorPlans/
  Organizations/
  Common/
  Validation/

EventPlatform.Infrastructure/
  Authentication/
  Persistence/
  Storage/
  Email/
  QrCodes/
  Imports/
  Exports/

EventPlatform.Api/
  Controllers/
  Middleware/
  Filters/
  Configuration/
```

Use:

- Dependency injection
- Async database operations
- Cancellation tokens
- DTOs
- FluentValidation
- Centralized exception handling
- Problem Details responses
- Structured logging

Avoid:

- Generic repositories that duplicate EF Core without adding value
- Premature event buses
- Microservices
- Complex CQRS infrastructure for simple CRUD
- Unnecessary abstractions

Lightweight command/query organization is acceptable.

---

## 19. Error Response Standard

Use RFC-compatible Problem Details.

Example:

```json
{
  "type": "https://platform-domain.com/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "detail": "One or more fields are invalid.",
  "errors": {
    "name": [
      "Event name is required."
    ]
  },
  "traceId": "..."
}
```

---

## 20. Logging and Audit

Use structured logs.

Log:

- Authentication failures
- Event creation
- Event publishing
- Guest imports
- Bulk guest assignment
- Floor plan changes
- User invitation
- Permission changes
- File upload failures
- Public search abuse or rate-limit events
- Unexpected application errors

Do not log:

- Passwords
- Tokens
- Full guest search payloads when privacy is a concern
- Sensitive guest notes

---

## 21. Analytics for MVP

Track lightweight event analytics:

- Welcome page views
- Guest searches
- Successful guest matches
- Unsuccessful searches
- Seat-result views
- Floor-plan views
- QR-origin visits where measurable
- Most common failed search terms, stored carefully
- Unique sessions, approximately

Admin metrics:

```text
Total guests
Assigned guests
Unassigned guests
Search success rate
Total public visits
Floor-plan opens
```

Analytics should not delay the core user response.

---

## 22. Testing Requirements

### Backend Tests

- Event authorization
- Tenant isolation
- Guest search normalization
- Duplicate name behavior
- Table capacity validation
- Cross-event assignment prevention
- Publishing validation
- Public token access
- Import validation
- Floor plan ownership validation

### Frontend Tests

- Welcome page rendering
- Search validation
- Search result selection
- Duplicate result selection
- Seat-result rendering
- Highlighted floor-plan table
- Event not found
- Event not published
- Guest not found
- Admin access restrictions
- Guest import error handling

### End-to-End Critical Flow

```text
Admin creates event
Admin uploads guest list
Admin creates tables
Admin assigns guest
Admin designs floor plan
Admin publishes event
Guest opens QR URL
Guest searches name
Guest selects result
Guest sees assigned table
Guest opens floor plan
Assigned table is highlighted
```

---

## 23. Seed Data

Create development seed data for:

### Organization

```text
Name: Demo Events
```

### Admin User

```text
Email: admin@demo.local
Password: configured only in development secrets
```

### Demo Event

```text
Name: licha & Roula's Wedding
Slug: licha-and-roula
Venue: The Olive Garden Venue
Theme: Earthy beige, olive green, warm bronze
```

### Demo Tables

```text
Table 1 — Olive
Table 2 — Cedar
Table 3 — Jasmine
Table 4 — Terracotta
Table 5 — Rose
```

### Demo Guests

Add enough demo guests to test:

- Exact matches
- Partial matches
- Duplicate names
- Arabic names
- Latin transliterations
- Unassigned guests
- Assigned guests
- Guests with seat numbers

---

## 24. MVP Scope

The first release should include:

- User authentication
- Roles and permissions
- Organization separation
- Event creation
- Event branding
- Public event welcome page
- Guest list manual management
- Excel or CSV guest import
- Guest search
- Table management
- Guest-to-table assignment
- Optional seat number
- Floor plan designer
- Mobile floor plan
- Publish and unpublish
- QR code generation
- Basic analytics
- Audit logs for key actions

Do not delay the MVP for:

- Ticketing
- Payments
- Full RSVP system
- Social features
- AI seating optimization
- Live chat
- Complex subscription billing
- Native mobile apps
- Microservices
- Advanced check-in hardware

---

## 25. Future Features

Design the system so these can be added later:

- RSVP management
- Invitation links
- Guest check-in
- QR check-in
- Meal preferences
- Dietary restrictions
- Event schedules
- Event gallery
- Guest photo uploads
- Live announcements
- Push notifications
- Email and SMS reminders
- WhatsApp integration
- Personalized guest welcome pages
- Multi-language event content
- Seating recommendation engine
- Drag-and-drop guest seating
- Household grouping
- Table balancing
- Venue template library
- Reusable event templates
- Custom domains
- Subscription plans
- White labeling
- Event planner agency accounts
- Sponsor sections
- Menu display
- Gift registry links
- Digital guestbook

---

## 26. Internationalization

The MVP should be prepared for localization.

Initial language priority:

- English
- Arabic

Requirements:

- Store UI translations separately
- Support RTL layouts
- Do not hardcode visible text inside reusable components
- Allow event-specific public content in multiple languages later
- Ensure guest names support Unicode
- Test Arabic search and rendering
- Ensure floor-plan labels work in RTL and LTR contexts

---

## 27. Performance Targets

Public pages should be optimized for mobile.

Targets:

- Fast first load on 4G
- Compressed responsive images
- Lazy-load noncritical media
- Cache published event configuration
- Cache public floor-plan data
- Avoid large JavaScript bundles
- Debounce guest search
- Paginate admin data
- Use database indexes
- Avoid loading complete guest lists publicly
- Use CDN-backed image delivery

Recommended indexes:

```text
Event: OrganizationId, Slug, Status
Guest: EventId, NormalizedSearchName
GuestSearchAlias: GuestId, NormalizedAlias
EventTable: EventId
EventMember: EventId, UserId
AuditLog: OrganizationId, EventId, CreatedAt
```

---

## 28. Accessibility

- Ensure sufficient color contrast
- Do not rely only on color to indicate the assigned table
- Support keyboard navigation
- Use semantic form labels
- Add accessible error messages
- Use visible focus states
- Provide alternative text for images
- Support screen readers
- Use large touch targets
- Ensure zoom controls have accessible labels
- Respect reduced-motion preferences

---

## 29. Development Rules for Codex

When implementing this project:

1. Work incrementally.
2. Do not rewrite unrelated files.
3. Preserve existing project conventions.
4. Prefer simple maintainable code.
5. Add migrations for database changes.
6. Add tests for important business rules.
7. Keep secrets out of source control.
8. Add environment-variable examples.
9. Use clear naming.
10. Avoid placeholder implementations in completed features.
11. Validate authorization in the backend.
12. Keep public and admin DTOs separate.
13. Do not expose internal entity models directly.
14. Use normalized floor-plan coordinates.
15. Use random public guest tokens.
16. Do not expose the complete guest list publicly.
17. Add loading, empty, and error states.
18. Keep the public flow usable without sign-in.
19. Make all public pages mobile-first.
20. Document setup and deployment steps.

---

## 30. Environment Variables

Example backend environment variables:

```text
ASPNETCORE_ENVIRONMENT
ConnectionStrings__DefaultConnection
Jwt__Key
Jwt__Issuer
Jwt__Audience
Jwt__AccessTokenMinutes
Jwt__RefreshTokenDays
Storage__Provider
Storage__Bucket
Storage__Endpoint
Storage__AccessKey
Storage__SecretKey
Storage__PublicBaseUrl
Email__Provider
Email__FromAddress
Email__ApiKey
App__PublicBaseUrl
App__AdminBaseUrl
Cors__AllowedOrigins__0
```

Example frontend environment variables:

```text
VITE_API_BASE_URL
VITE_PUBLIC_APP_URL
VITE_APP_NAME
```

Provide `.env.example` files without real secrets.

---

## 31. Initial Implementation Phases

### Phase 1 — Foundation

- Create solution structure
- Configure database
- Add organization and user entities
- Add authentication
- Add roles and authorization
- Add event CRUD
- Add global error handling
- Add logging
- Add migrations
- Add seed data

### Phase 2 — Guest and Seating Management

- Add guests
- Add guest groups
- Add tables
- Add assignments
- Add capacity validation
- Add import validation
- Add import execution
- Add export
- Add search aliases

### Phase 3 — Public Guest Experience

- Build welcome page
- Build guest search
- Build duplicate-name selection
- Build seat result page
- Add privacy-safe public DTOs
- Add public tokens
- Add rate limiting

### Phase 4 — Floor Plan

- Add floor plan entities
- Add designer
- Add draggable objects
- Add table linking
- Add normalized coordinates
- Add mobile public viewer
- Add highlighted table
- Add zoom and pan

### Phase 5 — Branding and Publishing

- Add theme editor
- Add image uploads
- Add preview
- Add publish validation
- Add public slug
- Add QR generation
- Add caching

### Phase 6 — Quality and Deployment

- Add analytics
- Add audit logs
- Add automated tests
- Improve accessibility
- Optimize performance
- Add Docker support
- Add deployment documentation
- Deploy frontend, API, database, and storage

---

## 32. Definition of Done

A feature is complete only when:

- Backend logic is implemented
- Authorization is enforced
- Validation is implemented
- Database migration is included
- Frontend states are handled
- Error messages are user-friendly
- Relevant tests are added
- Mobile behavior is verified
- No secrets are committed
- Documentation is updated
- The feature works in the complete user flow

---

## 33. Final Product Experience

The public experience should feel effortless.

A guest should be able to scan a QR code, find their name, and locate their seat in a few seconds without creating an account or learning how to use the platform.

The administrative experience should let an event planner go from a spreadsheet and venue layout to a polished, branded, published guest experience with minimal effort.

The MVP must prioritize:

```text
Simplicity
Speed
Mobile usability
Privacy
Reliable search
Clear seating information
Easy event setup
Low operating cost
Maintainable architecture
```
