# School Admission System

A browser-based school admission assignment with time-limited seat reservations,
simulated payment confirmation, and PostgreSQL transactions that prevent overselling.

- **Backend:** ASP.NET Core / .NET 8, EF Core 8, Npgsql
- **Database:** PostgreSQL 16 with a persistent named volume
- **Frontend:** React 18, TypeScript, Vite, CSS, Lucide
- **Runtime:** Docker Compose; no host .NET SDK, Node.js, or npm required

## Start the application

Install Docker Desktop with Docker Compose v2, then run:

```bash
git clone https://github.com/arafashrafee/school-admission-system.git
cd school-admission-system
docker compose up --build -d
docker compose ps
```

The first build downloads dependencies. Backend startup applies EF migrations and
seeds **Class 1** with 50 seats only when the class table is empty. No `.env` file
is required for the documented local defaults. Ports 5173, 8080, and 5432 must be free.

| Service | URL |
|---|---|
| Admission frontend | http://localhost:5173 |
| Backend API | http://localhost:8080 |
| Swagger API documentation | http://localhost:8080/swagger |
| Database readiness check | http://localhost:8080/health |

The health endpoint returns HTTP 200 with `status: Healthy` and
`database: Connected` when PostgreSQL is ready. CORS permits the frontend origins
`http://localhost:5173` and `http://127.0.0.1:5173`.

## Use the browser

**Admin:** open Admin, enter a combined class/shift name such as
`Class 3 · Morning`, and a positive whole-number seat total. Create the offering;
it appears in the live seat summary and student class list. Names are trimmed,
case-sensitive, unique, and at most 100 characters. Admin access is intentionally
public for this assignment.

**Student:** enter a 17-digit Bangladesh birth registration number using English
digits (0–9). Leading zeroes are preserved. The browser remembers this identifier;
use **Switch student** or **Clear identity** on shared devices. This is identifier-based
access, not secure authentication: anyone knowing the identifier can access its applications.

1. Browse **Available classes**. Backend-supplied class/shift names and seat counts
   refresh every four seconds, with manual refresh and loading/error feedback.
2. Choose **Apply for a seat**. Full classes and existing active applications have
   disabled apply controls. A successful reservation opens **My applications**.
3. Watch the countdown. It uses the backend's UTC `expiresAt` deadline and the
   browser clock every second; reopening the page refetches applications and does
   not restart the timer. Expired holds cannot be confirmed.
4. Choose **Confirm Admission** before expiry. Payment is simulated; no real
   charges or external payment provider are involved. Confirmation is idempotent.
5. To release an active hold, choose **Cancel reservation** and confirm the dialog.
   Confirmed admissions cannot be cancelled. Expired/cancelled holds can be reapplied
   for when seats are available.

The application shows HELD, CONFIRMED, EXPIRED, and CANCELLED records. Identity
changes clear the previous student's visible applications and obsolete requests.
Polling recovers after network failures. An aborted mutation may already have reached
server-side commit, so the UI refetches authoritative state rather than assuming rollback.

## Run all automated tests with Docker

Start the application first. The optional test profile waits for backend readiness,
so migrations also exist on a fresh database. Tests create uniquely named fixtures
and delete only their own rows.

```bash
# Frontend: identifier/persistence, countdown, status rendering, API handling
docker compose exec -T frontend npm test

# TypeScript and production frontend build
docker compose exec -T frontend npm run build

# Full PostgreSQL-backed backend suite, including HTTP contract and concurrency tests
docker compose --profile test run --build --rm tests

# The HTTP contract suite against the running backend process
docker compose --profile test run --rm -e ADMISSION_LIVE_URL=http://backend:8080 tests --filter FullyQualifiedName~AdmissionApiTests
```

Frontend tests use Node's built-in runner and TypeScript. Backend tests use xUnit
with real PostgreSQL, not an in-memory database. Coverage includes 100 parallel HTTP
reservations for exactly 10 seats (10 created, 90 conflicts), a separate service
stress test, duplicate applications, ownership, idempotency, cancellation, expiry,
and confirm/cancel/sweep races. Test classes share a bounded connection pool and run
sequentially; individual stress tests still issue 100 requests concurrently.

For optional host-based frontend development, install Node.js 20.19+ and run
`npm ci`, `npm test`, `npm run build`, and `npm run dev` in `frontend/`.
With a local .NET 8 SDK, set `ConnectionStrings__DefaultConnection` to a reachable
migrated PostgreSQL database and run `dotnet test SchoolAdmissionSystem.sln`.

## Exact API contract

These are the eight business operations. `/` and `/health` are informational endpoints.
There is no `/api` prefix. IDs are stable UUID strings; timestamps are UTC ISO 8601;
status strings are `HELD`, `CONFIRMED`, `EXPIRED`, and `CANCELLED`.

| Method | Path | Success | Exact response fields |
|---|---|---|---|
| POST | `/classes` | 201 | `id`, `name`, `totalSeats` |
| GET | `/classes` | 200 array | `id`, `name`, `totalSeats`, `heldSeats`, `confirmedSeats`, `availableSeats` |
| GET | `/classes/{classId}` | 200 | Same fields as the class list |
| POST | `/classes/{classId}/holds` | 201 | `holdId`, `classId`, `studentId`, `status`, `expiresAt` |
| GET | `/holds/{holdId}` | 200 | `holdId`, `classId`, `status`, `expiresAt` |
| POST | `/holds/{holdId}/confirm` | 200 | `holdId`, `status` |
| DELETE | `/holds/{holdId}` | 204 | Empty body |
| GET | `/me/holds` | 200 array | `holdId`, `classId`, `className`, `status`, `expiresAt` |

`X-Student-Id` is required for creation of a hold, confirmation, cancellation, and
`/me/holds`; it contains exactly 17 ASCII digits. Identity is never taken from URL
parameters, query strings, or request bodies. These operations need no request body.

```bash
curl -i -X POST http://localhost:8080/classes \
  -H 'Content-Type: application/json' \
  -d '{"name":"Class 3 · Morning","totalSeats":30}'
curl -i http://localhost:8080/classes

# Replace UUID placeholders with IDs returned by the API.
curl -i -X POST 'http://localhost:8080/classes/<classId>/holds' \
  -H 'X-Student-Id: 00000000000000001'
curl -i http://localhost:8080/me/holds -H 'X-Student-Id: 00000000000000001'
curl -i -X POST 'http://localhost:8080/holds/<holdId>/confirm' \
  -H 'X-Student-Id: 00000000000000001'
curl -i -X DELETE 'http://localhost:8080/holds/<holdId>' \
  -H 'X-Student-Id: 00000000000000001'
```

Creation returns a `Location` header. Business errors have `{ "code": "...",
"message": "..." }`: 400 for invalid input/header, 403 for a different owner,
404 for a missing class/hold, and 409 for no seats, duplicates, or invalid lifecycle
transitions. Binding errors use standard ASP.NET Core 4xx responses. Unexpected
server failures return 500 without database exception details in business responses.

## Persistence, concurrency, and expiration

PostgreSQL stores classes and holds in the `postgres_data` named volume. Container
restarts retain records. Stop/resume safely with `docker compose stop` and
`docker compose start`; `docker compose down` also preserves the named volume.
Do not delete volumes when preserving application data.

Reservation, confirmation, and cancellation serialize on the parent class row with
`SELECT ... FOR UPDATE`. Allocation sweeps overdue holds, checks duplicate active
applications, counts occupied seats, and inserts a new hold in one transaction.
Database checks and a partial unique index protect student format, statuses, positive
capacity, and active student/class uniqueness. See [CONCURRENCY.md](CONCURRENCY.md).

The background worker persists expired status every 15 seconds without requiring
API traffic. Read responses/countdowns treat clock-expired holds as expired
immediately; allocation also performs an eager sweep before deciding capacity.
Confirmation is owner-checked and idempotent. No client seat counter decides capacity.

## Configuration and limitations

Optionally copy `.env.example` to `.env` before startup. Defaults are local demo
values, including `postgres` as the database user/password; use suitable credentials
before any shared deployment. Existing volumes retain their original database credentials.

| Variable | Default | Purpose |
|---|---|---|
| `POSTGRES_DB` | `school_admission` | Database name |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` | `postgres` / `postgres` | Local database credentials |
| `POSTGRES_PORT` | `5432` | Host database port |
| `HOLD_DURATION_SECONDS` | `600` | Reservation duration in seconds |
| `ASPNETCORE_ENVIRONMENT` | `Development` | Enables local Swagger |
| `VITE_API_URL` | `http://localhost:8080` | Public browser API base URL |

Compose derives the backend/test connection string from the database variables.
Never put secrets in `VITE_` values. Compose runs a Vite development server and uses
its runtime environment; a separately hosted production `dist` bundle would need
`VITE_API_URL` at build time. Production hosting/authentication are outside the assignment.
The countdown assumes an accurate device clock. Lists are unpaginated for this
assignment. Identity and public Admin access are demo assumptions, not authorization
for a production service. See [ASSUMPTIONS.md](ASSUMPTIONS.md) and [AI_USAGE.md](AI_USAGE.md).
