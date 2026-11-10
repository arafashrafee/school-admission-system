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

The assignment exposes exactly these eight business operations, without a prefix.
IDs are stable UUID strings. Timestamps are UTC ISO 8601 strings. Status values
are `HELD`, `CONFIRMED`, `EXPIRED`, and `CANCELLED`.

| Method | Path | Success | Exact response fields |
|---|---|---|---|
| POST | `/classes` | 201 | `id`, `name`, `totalSeats` |
| GET | `/classes` | 200 array | `id`, `name`, `totalSeats`, `heldSeats`, `confirmedSeats`, `availableSeats` |
| GET | `/classes/{classId}` | 200 | Same class fields as the list |
| POST | `/classes/{classId}/holds` | 201 | `holdId`, `classId`, `studentId`, `status`, `expiresAt` |
| GET | `/holds/{holdId}` | 200 | `holdId`, `classId`, `status`, `expiresAt` |
| POST | `/holds/{holdId}/confirm` | 200 | `holdId`, `status` |
| DELETE | `/holds/{holdId}` | 204 | Empty body |
| GET | `/me/holds` | 200 array | `holdId`, `classId`, `className`, `status`, `expiresAt` |

`POST /classes` accepts `{ "name": "Class 2", "totalSeats": 30 }`. Names are
trimmed, case-sensitive, unique, and 1–100 characters long. Seat totals must be
positive. Creation responses include `Location` pointing to the new resource.

**Student identity:** `X-Student-Id` is required for hold creation, confirmation,
cancellation, and `/me/holds`. It must contain exactly 17 ASCII decimal digits,
preserving leading zeroes. Identity is never read from URL parameters or request
bodies. These operations need no request body. `/me/holds` returns only the header
student's holds, newest first, and an empty array when none exist.

```bash
curl -i -X POST http://localhost:8080/classes \
  -H 'Content-Type: application/json' -d '{"name":"Class 2","totalSeats":30}'
curl -i -X POST 'http://localhost:8080/classes/<classId>/holds' \
  -H 'X-Student-Id: 00000000000000001'
curl -i http://localhost:8080/me/holds -H 'X-Student-Id: 00000000000000001'
```

Business errors return `{ "code": "...", "message": "..." }` with HTTP 400 for
invalid input or a missing/invalid identity header, 403 for a mismatched owner,
404 for a missing class/hold, and 409 for duplicate classes/applications, no seats,
or invalid lifecycle transitions. Malformed JSON and invalid route parameter
syntax use ASP.NET Core's standard 4xx responses. Unexpected server failures
return HTTP 500 without database exception details.

Confirmation and cancellation are idempotent for the owning student. Confirmed
holds cannot be cancelled. Clock-expired holds appear as `EXPIRED` and release
available capacity before the background worker persists the transition. Existing
PostgreSQL transactions, class row locks, and uniqueness constraints remain the
capacity authority. The assignment excludes authentication: the header checks
ownership but does not prove identity.

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
confirm/cancel races. HTTP tests assert exact methods, routes, response fields, status codes, UTC expiry,
header validation, student isolation, concurrent class creation, and 100 parallel
HTTP hold requests against 10 seats. All existing service concurrency tests remain.

With a local .NET 8 SDK and a reachable database, set
`ConnectionStrings__DefaultConnection` and run `dotnet test SchoolAdmissionSystem.sln`.

To execute the HTTP contract suite against the running Compose backend:

```bash
docker run --rm --network school-admission-system_default \
  -e ADMISSION_LIVE_URL=http://backend:8080 school-admission-tests \
  --filter FullyQualifiedName~AdmissionApiTests
```

The same assertions verify live routes, exact response fields, identity isolation,
and parallel allocation. Tests clean up only the classes and holds they create.

The stress-test classes run sequentially and share a PostgreSQL connection pool
to stay within the database connection limit while the live backend is running.
Each stress test still submits 100 requests in parallel.
