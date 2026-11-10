# School Admission System

A robust, enterprise-grade School Admission System designed for seat reservation and admission application processing.

---

## Tech Stack & Architecture

- **Backend:** C# ASP.NET Core Web API (.NET 8) with Entity Framework Core
- **Database:** PostgreSQL 16 with persistent Docker storage volume
- **Frontend:** React 18+, Vite, TypeScript, and Lucide Icons
- **Orchestration:** Docker Compose multi-service architecture

---

## System Ports & Endpoints

| Component | URL / Port | Description |
|---|---|---|
| **Frontend** | `http://localhost:5173` | React Landing & Status Dashboard |
| **Backend API** | `http://localhost:8080` | ASP.NET Core Web API |
| **Swagger UI** | `http://localhost:8080/swagger` | OpenAPI Interactive Documentation |
| **Health Check** | `http://localhost:8080/health` | Live DB connectivity & readiness status |
| **PostgreSQL** | `localhost:5432` | Relational Database (Internal & Host mapped) |

---

## Prerequisites

- **Docker Desktop** (Engine 20.10+ / Compose v2+)
- *Note:* Neither .NET SDK nor Node.js is required on the host machine. Multi-stage Docker builds handle all dependency resolution, compilation, and bundling in containerized environments.

---

## Quickstart

1. **Clone & Enter Directory:**
   ```bash
   cd ~/Desktop/Projects/school-admission-system
   ```

2. **Environment Configuration:**
   Copy the example environment file if not already present:
   ```bash
   cp .env.example .env
   ```

3. **Build & Start Services:**
   ```bash
   docker compose up --build -d
   ```

4. **Verify Container Health:**
   ```bash
   docker compose ps
   ```

5. **Test Backend & Database Readiness:**
   ```bash
   curl -i http://localhost:8080/health
   ```
   Expected response:
   ```json
   {
     "status": "Healthy",
     "database": "Connected",
     "holdDurationSeconds": 600,
     "timestamp": "..."
   }
   ```

6. **Access Frontend:**
   Open [http://localhost:5173](http://localhost:5173) in your browser.

---

## Configuration & Environment Variables

| Variable | Default | Purpose |
|---|---|---|
| `POSTGRES_DB` | `school_admission` | Name of the PostgreSQL database |
| `POSTGRES_USER` | `postgres` | Database superuser username |
| `POSTGRES_PASSWORD` | `postgres` | Database superuser password |
| `POSTGRES_PORT` | `5432` | Host port mapped to PostgreSQL container |
| `ASPNETCORE_ENVIRONMENT` | `Development` | ASP.NET Core runtime environment |
| `ConnectionStrings__DefaultConnection` | `Host=postgres;...` | Npgsql database connection string |
| `HOLD_DURATION_SECONDS` | `600` | Seat hold duration (default: 10 minutes) |
| `VITE_API_URL` | `http://localhost:8080` | Frontend target API endpoint (public URL only, no secrets) |

> **Security Rule:** Never include secrets or credentials in variables prefixed with `VITE_`, as they are compiled directly into client-side bundles.

---

## Stopping & Teardown

To stop the running containers:
```bash
docker compose down
```

To stop containers and remove persistent database volumes (reset data):
```bash
docker compose down -v
```

---

## Project Structure

```
school-admission-system/
├── backend/
│   ├── Data/
│   │   └── ApplicationDbContext.cs
│   ├── Program.cs
│   ├── SchoolAdmissionSystem.Api.csproj
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── Dockerfile
│   └── .dockerignore
├── frontend/
│   ├── src/
│   │   ├── App.css
│   │   ├── App.tsx
│   │   ├── index.css
│   │   ├── main.tsx
│   │   └── vite-env.d.ts
│   ├── index.html
│   ├── package.json
│   ├── tsconfig.json
│   ├── tsconfig.node.json
│   ├── vite.config.ts
│   ├── Dockerfile
│   └── .dockerignore
├── .env.example
├── .gitignore
├── AI_USAGE.md
├── ASSUMPTIONS.md
├── docker-compose.yml
└── README.md
```

## Phase 2 Admission APIs

All eight business APIs use the `/api` prefix. Student IDs are JSON strings of
exactly 17 ASCII digits, including any leading zeroes. Status values are strings:
`HELD`, `CONFIRMED`, `EXPIRED`, or `CANCELLED`. Timestamps are UTC.

| Method | Path | Request / response |
|---|---|---|
| POST | `/api/classes` | `{ "name": "Class 2", "totalSeats": 30 }` → 201 with seat counts |
| GET | `/api/classes` | All classes with total, held, confirmed, and available seats |
| GET | `/api/classes/{classId}/seats` | Seat counts for one class |
| POST | `/api/holds` | `{ "classId": "<uuid>", "studentId": "00000000000000001" }` → 201 with hold |
| POST | `/api/holds/{holdId}/confirm` | `{ "studentId": "00000000000000001" }` → confirmed hold; repeat is idempotent |
| POST | `/api/holds/{holdId}/cancel` | `{ "studentId": "00000000000000001" }` → cancelled hold |
| GET | `/api/holds/{holdId}` | Hold status and expiration timestamps |
| GET | `/api/students/{studentId}/holds` | Student's holds, newest first; empty array if none |

Class names are trimmed, case-sensitive, unique, and 1–100 characters long.
Seat totals must be positive. Successful creation returns a `Location` header.
Hold responses include `holdId`, `classId`, `studentId`, `status`, `createdAt`,
`expiresAt`, `confirmedAt`, and `isExpiredAccordingToClock`.

Business errors return `{ "code": "...", "message": "..." }` with HTTP 400 for
invalid input, 403 for a mismatched owner, 404 for a missing class/hold, and 409
for duplicate classes/applications, full classes, or invalid lifecycle transitions.
Unexpected server failures return HTTP 500 without database exception details.
Malformed JSON and invalid route parameter syntax use ASP.NET Core's standard responses.

Clock-expired holds appear as `EXPIRED` and no longer consume available seats,
even before the background worker persists the transition. These APIs inherit the
assignment's no-authentication scope: the supplied student ID is an ownership
check, not proof of identity.

### Run the full test suite with Docker

Start PostgreSQL, then build and run the test runner:

```bash
docker compose up -d postgres
docker build -f tests/Dockerfile -t school-admission-tests .
docker run --rm --network school-admission-system_default school-admission-tests
```

The default test connection matches the Compose defaults. With custom credentials,
pass `-e ConnectionStrings__DefaultConnection='Host=postgres;Database=...;Username=...;Password=...'`
to `docker run`. Tests use uniquely named classes and remove only their own rows;
they never reset the database. HTTP integration tests apply migrations on startup.
Service tests include 100 parallel allocations against 10 seats, duplicate
applications, expiration, ownership, cancellation, confirmation idempotency, and
confirm/cancel races. HTTP tests cover all eight APIs and concurrent class creation.

With a local .NET 8 SDK and a reachable database, set
`ConnectionStrings__DefaultConnection` and run `dotnet test SchoolAdmissionSystem.sln`.
