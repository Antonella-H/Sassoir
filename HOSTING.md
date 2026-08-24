# Sassoir Hosting Checklist

This project is ready for a split production setup:

- Frontend: Render Static Site, Cloudflare Pages, Netlify, or similar.
- API: Render Web Service running ASP.NET Core 8.
- Database: your managed PostgreSQL database.
- DNS: Cloudflare nameservers for the GoDaddy domain.

## Recommended Domains

```text
sassoir.com      -> frontend
www.sassoir.com  -> frontend
api.sassoir.com  -> API
```

## API Service

Create a Render Web Service for the API. Choose `Docker` when Render asks for the language/runtime.

```text
Language: Docker
Root directory: api
Dockerfile path: ./Sassoir.Api/Dockerfile
```

Set these environment variables on the API service:

```text
ASPNETCORE_ENVIRONMENT=Production
DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false
ConnectionStrings__DefaultConnection=YOUR_RENDER_POSTGRES_EXTERNAL_DATABASE_URL
Auth__Issuer=sassoir
Auth__Audience=sassoir.admin
Auth__SigningKey=GENERATE_A_RANDOM_SECRET_AT_LEAST_32_CHARACTERS
Auth__AccessTokenMinutes=30
Auth__SeedAdminEmail=YOUR_ADMIN_EMAIL
Auth__SeedAdminPassword=YOUR_FIRST_ADMIN_PASSWORD
Cors__AllowedOrigins=https://sassoir.com,https://www.sassoir.com
Database__MaxPoolSize=20
Database__CommandTimeoutSeconds=30
RateLimiting__PublicEventPerMinute=60
RateLimiting__GuestSearchPerMinute=30
RateLimiting__SeatResultPerMinute=30
RateLimiting__GuestMessagePerMinute=5
Uploads__RootPath=/var/data/uploads
```

The API also includes `https://sassoir.com` and `https://www.sassoir.com` as built-in allowed origins, but keep `Cors__AllowedOrigins` set on Render so any future frontend domain changes are explicit.

If you do not add a persistent disk yet, leave `Uploads__RootPath` unset. Uploaded images may be lost after redeploys or restarts without persistent storage.

After the API deploys, test:

```text
https://api.sassoir.com/api/health
https://api.sassoir.com/api/health/live
https://api.sassoir.com/api/health/ready
```

Apply the production performance indexes to PostgreSQL after the first deploy:

```text
database/migrations/20260717_performance_indexes.sql
```

## Frontend Service

Create a Render Static Site for the frontend.

```text
Root directory: web
Build command: npm install && npm run build
Publish directory: dist
```

Set this environment variable on the frontend service:

```text
VITE_API_BASE_URL=https://api.sassoir.com
```

## Cloudflare DNS

Point the GoDaddy domain to Cloudflare nameservers first. Then add DNS records from Render.

Typical records look like this, but use the exact values Render gives you:

```text
CNAME  @    YOUR_FRONTEND_HOST.onrender.com
CNAME  www  YOUR_FRONTEND_HOST.onrender.com
CNAME  api  YOUR_API_HOST.onrender.com
```

Keep Cloudflare proxy off while Render validates custom domains. You can turn proxying on later if needed.

## Launch Notes

- Change the seeded admin password after first login.
- Keep `Auth__SigningKey` secret and do not commit it.
- Use a paid database plan before real events.
- Add persistent storage or Cloudflare R2 before relying on image uploads.
