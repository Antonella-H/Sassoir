# Sassoir

DLP-style full-stack solution for the event seating and guest experience platform.

## Structure

```text
api/
  Sassoir.sln
  Directory.Build.props
  Sassoir.Api/
    Sassoir.Api.csproj
    Program.cs
    SeedData.cs
web/
  package.json
  vite.config.ts
  src/
    App.tsx
    main.tsx
    styles.css
database/
  schema.sql
  seed.sql
docker-compose.yml
```

## Start The API

```powershell
cd C:\Users\AntonellaHitti\source\repos\sassoir\api
dotnet run --project .\Sassoir.Api\Sassoir.Api.csproj --launch-profile http
```

API health check:

```text
http://127.0.0.1:5087/api/health
```

## Start The Frontend

```powershell
cd C:\Users\AntonellaHitti\source\repos\sassoir\web
npm install
npm run dev
```

Admin login:

```text
http://127.0.0.1:5173
```

Testing credentials:

```text
Email: admin@sassoir.com
Password: P@$$w0rd
```

The API seeds this admin user into PostgreSQL on first use when `Auth__SeedAdminEmail` and `Auth__SeedAdminPassword` are configured.

Public event experience:

```text
http://127.0.0.1:5173/e/lichaa-and-roula
```

Admin dashboard:

```text
http://127.0.0.1:5173/admin
```

The frontend proxies `/api` to `http://127.0.0.1:5087`, just like the DLP Vite setup.

## Start The Database

```powershell
cd C:\Users\AntonellaHitti\source\repos\sassoir
docker compose up -d postgres
```

The database initializes from `database/schema.sql` and `database/seed.sql`. The API uses EF Core with the PostgreSQL provider and reads `ConnectionStrings__DefaultConnection`.
The local Docker database is exposed on host port `55432` to avoid colliding with any existing PostgreSQL server on `5432`.

## Hosting

See [HOSTING.md](HOSTING.md) for the production deployment checklist, including Render service settings, domain/DNS setup, and required environment variables.

## Current State

- Public event and admin event CRUD APIs are backed by PostgreSQL through EF Core.
- Frontend is a Vite React app.
- PostgreSQL schema and seed data are ready.
- Admin authentication is database-backed with PBKDF2 password hashes and signed short-lived access tokens.
- Before production, change `Auth__SigningKey`, remove or rotate the seeded admin password, and provide real secret values through environment configuration.
