# Sassoir Performance Audit

Date: 2026-07-17

## Scope

Reviewed the ASP.NET Core minimal API backend, EF Core query patterns, PostgreSQL schema/bootstrap SQL, React public guest flow, admin list flows, and Render hosting notes.

## Findings

### Critical

- Public guest search loaded matching guests plus aliases and then ranked in memory. This risked unnecessary memory and CPU during the expected 150-guest burst.
- Public seat lookup and floor-plan lookup required separate API calls and could load broader event graphs than the guest portal needs.
- Public endpoints were not rate limited.

### High

- Public event and floor-plan data changed infrequently but was fetched from PostgreSQL for every request.
- Many API methods were synchronous, so busy request periods could tie up request threads.
- Admin list endpoints returned unbounded arrays. This is workable for the seed dataset but risky for larger events.
- PostgreSQL had useful baseline indexes, but lacked trigram indexes for name search and some join/filter indexes for public lookups and messages.

### Medium

- The React public search flow debounced requests but did not cancel stale fetches.
- Public event/floor-plan data was not cached client-side.
- Response compression was not configured in the API.
- Health checks used one generic endpoint and did not distinguish liveness from readiness.
- Npgsql relied on default pool sizing, which can be too high for smaller Render/PostgreSQL plans.

### Low

- The backend is currently a single large minimal API file. Route groups now separate public/admin surfaces, but further modularization would improve maintainability.
- Admin screens still use compatible array endpoints in the current UI. Paginated API endpoints are available for the next UI pass.

## Implemented Changes

- Added public/admin API route grouping for the optimized public routes and new admin paginated routes.
- Reworked public event, public floor-plan, guest search, seat result, guest floor-plan, search metrics, and guest-message paths to async methods with cancellation-token support.
- Replaced public guest search in-memory ranking with bounded database filtering, deterministic ordering, `AsNoTracking`, DTO projection, and max 10 results.
- Added combined public seat response containing event data, companions, floor plan, and highlighted object ID.
- Added `IMemoryCache` for public event and published floor-plan responses with immediate invalidation after event, table, or floor-plan admin changes.
- Added Brotli/Gzip response compression.
- Added endpoint-specific fixed-window rate limits.
- Added liveness and readiness health endpoints.
- Added correlation ID response headers and structured request-duration logging.
- Added conservative Npgsql pool and command-timeout defaults.
- Added public upload cache headers.
- Added PostgreSQL trigram/search/join/message indexes in schema bootstrap and a standalone SQL migration.
- Added React AbortController cancellation for public event load, guest search, and seat lookup.
- Added client-side public event/floor-plan cache.
- Added k6 load-test script for public event, search, seat, mixed-admin/public, and 200-user safety-margin scenarios.

## Slow or Risky Endpoints

- `POST /api/public/events/{slug}/guests/search`: previously the highest public risk; now bounded and async.
- `GET /api/public/events/{slug}/guests/{publicToken}`: previously only returned seat data; now includes the floor-plan payload to avoid sequential public calls.
- `GET /api/admin/events/{id}/guests`: still compatible and unbounded for the existing UI; use `/api/admin/events/{id}/guests/page` for large events.
- `GET /api/admin/events/{id}/tables`: still compatible and unbounded for the existing UI; use `/api/admin/events/{id}/tables/page`.
- `GET /api/admin/events/{id}/messages`: still compatible and unbounded for the existing UI; use `/api/admin/events/{id}/messages/page`.

## Verification

- `dotnet build api\Sassoir.sln`: passed.
- `npm.cmd run build` in `web`: passed.

Load tests were not executed in this workspace because there is no confirmed running production-like PostgreSQL/API target in the local environment. Do not claim p95/p99 targets are met until `load-tests/public-flow.k6.js` is run against the Render API and the results are captured.

## Deployment Notes

1. Deploy the API to Render.
2. Apply `database/migrations/20260717_performance_indexes.sql` to the production PostgreSQL database.
3. Confirm Render API environment variables include:
   - `Cors__AllowedOrigins=https://sassoir.com,https://www.sassoir.com`
   - `Database__MaxPoolSize=20`
   - `Database__CommandTimeoutSeconds=30`
   - `RateLimiting__PublicEventPerMinute=60`
   - `RateLimiting__GuestSearchPerMinute=30`
   - `RateLimiting__SeatResultPerMinute=30`
   - `RateLimiting__GuestMessagePerMinute=5`
4. Check `/api/health/live` and `/api/health/ready`.
5. Run the k6 script against Render and save the results.

## Rollback Notes

- API rollback: redeploy the previous Render version.
- Database rollback: the added indexes are non-destructive. If needed, drop them by name from `database/migrations/20260717_performance_indexes.sql`.
- Frontend rollback: redeploy the previous static-site build.

## Remaining Bottlenecks

- Move the current admin UI to the new paginated endpoints for very large events.
- Add true background import jobs if CSV/Excel imports grow beyond a few thousand rows.
- Capture `EXPLAIN ANALYZE` from production-like PostgreSQL after the new indexes are applied.
- Consider distributed cache only if the API scales to multiple instances and public cache hit rate becomes important across instances.
