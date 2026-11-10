# Architectural Assumptions & Design Decisions

This document records the final assignment assumptions and the historical decisions established across its implementation phases.

---

## 1. Scope & Phasing
- **Phase 0 Scope:** Limited strictly to project scaffolding, containerization, environment configuration, database readiness verification, and initial front-to-back connectivity.
- **Completed scope:** Later phases implemented transactional seat allocation, the admission lifecycle, simulated confirmation, and functional student/Admin browser workflows.

## 2. Authentication & Authorization
- **No Login / Authentication:** In accordance with assignment constraints, user login, JWT tokens, and role-based access control (RBAC) are excluded to prioritize admission workflow correctness, concurrency handling, and clean code architecture.

## 3. Technology Stack & Framework Choices
- **Backend Framework:** ASP.NET Core Web API on .NET 8 (LTS) for maximum performance, strong typing, and enterprise standard tooling.
- **ORM:** Entity Framework Core 8 with Npgsql provider (`Npgsql.EntityFrameworkCore.PostgreSQL`).
- **Database:** PostgreSQL 16 (official alpine image) provides ACID compliance, strong transaction isolation (crucial for concurrency in seat reservations), and rock-solid reliability.
- **Simplicity First:** Redis, external message queues (e.g. RabbitMQ/Kafka), and microservices are explicitly avoided. All concurrency controls and seat holds can be modeled reliably with PostgreSQL transactions and timestamps without introducing distributed systems overhead.
- **Frontend Framework:** React 18+ with TypeScript and Vite. Vite offers instant HMR, fast compilation, and lightweight footprint.

## 4. Seat Hold Configuration
- **Variable Name:** `HOLD_DURATION_SECONDS`
- **Default Value:** `600` (10 minutes).
- **Injection:** Provided through environment variables and mapped into backend `IConfiguration`. Fallback is guaranteed if omitted.

## 5. Network & Port Allocations
- **Frontend:** Bound to port `5173` on host (`http://localhost:5173`).
- **Backend:** Bound to port `8080` on host (`http://localhost:8080`).
- **PostgreSQL:** Bound to port `5432` on host (`localhost:5432`) and accessible internally via Docker service name `postgres:5432`.
- **CORS:** Configured in ASP.NET Core to permit requests originating from `http://localhost:5173` and `http://127.0.0.1:5173`.

## 6. Containerization & Portability
- **Zero Host Dependency Requirement:** The entire application stack builds and runs cleanly via `docker compose up --build`. No local installation of .NET SDK or Node.js/npm is mandated on the host machine.
- **Data Durability:** A named Docker volume `postgres_data` persists database tables and records across container restarts.

## 7. Security & Confidentiality
- **Client Bundles:** Only non-sensitive URLs (`VITE_API_URL`) are prefixed with `VITE_`. Database passwords and secret keys are never exposed to the frontend.
- **Confidential Assets:** `*.pdf` files and assignment specification documents are explicitly ignored in `.gitignore` and must never be committed to source control or pushed externally.

## 8. Phase 1A Database Architecture & Concurrency
- **PostgreSQL Connection Pool:** Configured `max_connections=250` on PostgreSQL container to support sustained parallel reservation workloads.
- **StudentId Format:** Stored as `varchar(17)` with database check constraint `length("StudentId") = 17 AND "StudentId" ~ '^[0-9]{17}$'`. Preserves leading zeroes deterministically.
- **Hold Statuses:** Strictly constrained via check constraint to `HELD`, `CONFIRMED`, `EXPIRED`, `CANCELLED`.
- **Active Hold Uniqueness:** Partial unique index on `("ClassId", "StudentId") WHERE "Status" IN ('HELD', 'CONFIRMED')`. Non-time-dependent. Ensures active duplicate applications are blocked at the database level while expired holds do not block future applications.
- **Row-Level Serialization:** Allocation transactions acquire an exclusive lock on the parent `AdmissionClass` row via `SELECT ... FOR UPDATE`, ensuring database-atomic capacity enforcement without race conditions or in-memory caches.
- **Two-Tier Expiration:**
  1. *Eager Transactional Sweep:* Any allocation transaction for a class immediately sweeps its own expired holds (`ExpiresAt <= UtcNow`), freeing capacity instantly.
  2. *Periodic Background Sweeper:* `HoldExpirationWorker` sweeps all classes periodically (every 15s) to guarantee eventual consistency even during periods of low activity.

## 9. Phase 2 Assignment API Contract
- Business routes are exactly `POST /classes`, `GET /classes`, `GET /classes/{classId}`, `POST /classes/{classId}/holds`, `GET /holds/{holdId}`, `POST /holds/{holdId}/confirm`, `DELETE /holds/{holdId}`, and `GET /me/holds`.
- There is no `/api` prefix. Student identity comes only from the required `X-Student-Id` header on hold mutations and `/me/holds`, with exactly 17 ASCII digits.
- Public DTOs match the assignment fields exactly. Class responses use `id` and `name`; only student history includes `className`. UUIDs are stable strings, status values are strings, and expiry timestamps are UTC.
- Class creation and hold creation return 201; cancellation returns 204 with no body; reads and confirmation return 200. Full classes and invalid lifecycle transitions return 409.
- Class names use exact, case-sensitive uniqueness after trimming. Concurrent duplicates are resolved by the existing unique database index.
- The transactional service logic, repository capacity queries, database schema, persistent volume, and concurrency protections are unchanged by this contract hotfix.
- The assignment excludes authentication. The identity header is an ownership check, not verified authentication. The Phase 2 hotfix did not include frontend work.

## 10. Phase 3 Browser Experience
- A birth registration number identifies a student with exactly 17 ASCII digits. No login/password is introduced. Local storage retains only that identifier; disabled storage permits a page-only session with a visible notice.
- The identifier is supplied exclusively through `X-Student-Id` for the required endpoints. Switching identities clears stale visible applications and aborts/ignores obsolete work. It is not secure authentication and shared devices require clearing identity.
- Admin access is deliberately open in this assignment. Class and shift are represented by one backend class name; no separate shift schema or hardcoded seat availability is introduced.
- Classes and the current student's applications are refetched every four seconds with cleanup and cancellation. Active countdowns use the backend UTC deadline and the browser clock; browser clock accuracy is assumed.
- Confirmation simulates payment only. Cancellation requires a dialog and supports HTTP 204. Confirmed admissions expose no cancellation action.
- The frontend uses React 18, TypeScript, Vite, existing CSS, and Lucide. Node's test runner and TypeScript support focused tests without an additional runtime framework.
- PostgreSQL data, assignment API contracts, and transactional concurrency logic are unchanged. Phase 3 does not include production authentication, deployment, or Phase 4 work.

## 11. Final evaluator setup
- Docker Compose v2 is the only application prerequisite; initial builds need internet access to package/image registries and the documented host ports must be free.
- `.env` is optional for local defaults. Compose derives API/test connection strings from PostgreSQL variables; changing environment passwords does not reset an existing volume's credentials.
- The `test` profile waits for the backend health check, ensuring migrations exist before service tests on a fresh database. Test fixtures are removed by their owning tests; original application data and volumes are retained.
- Final clean-start verification uses a separate public clone, project namespace, ports, and new volume. QA logs/screenshots remain ignored local artifacts.

## 12. Optional bonus: two independent backend processes — verified

**Result: PASS.** Codex executed this isolated check on November 11, 2026
(Bangladesh time), against source commit `4e021b0df061c0da65e732078cb9f167573af645`.
Production business logic was not changed.

### Setup and procedure

- A separate Compose project, `admission-bonus-20261111`, used a new named
  `bonus_data` volume and PostgreSQL 16 database `bonus_admission`
  (`max_connections=250`). No existing application database or volume was reused.
- Two separate containers ran `dotnet SchoolAdmissionSystem.Api.dll` from the same
  freshly built backend image, `admission-bonus-backend:4e021b0`
  (image SHA-256 `b6cfdee2a3aec1e29e518f556fb8bc62b8d3768a2641de58a1f4506e74f8e304`).
  They had distinct Docker-host process IDs: API A `38560`, API B `38694`.
- API A listened on port 8081, published as `127.0.0.1:18081`; API B listened
  on 8082, published as `127.0.0.1:18082`. Both connected to the same isolated
  PostgreSQL service. Each had its own connection pool, capped at 100 connections.
- API A initialized migrations/seed data before API B started. Both passed their
  database-backed health checks before load began.
- A new class with exactly 10 seats was created through API A and read back through
  API B. Its UUID was `8c34dc53-4a3e-4c60-b8a1-8fdfd343dad6`.
- A containerized Node.js verifier prepared 100 requests with distinct 17-digit
  student IDs, alternated destinations (50 per API), and released them from one
  shared promise barrier. Each request used `POST /classes/{classId}/holds` with
  `X-Student-Id` and no body. There were no retries or batches.
- Concurrent read loops queried each API's class seat counts, waiting five
  milliseconds after each response before polling again. The verifier asserted
  `heldSeats + confirmedSeats <= totalSeats` on every snapshot, exact HTTP status
  totals, `CAPACITY_EXHAUSTED` on every conflict, and unique successful hold IDs.
- Direct SQL then checked all stored allocations for that class and matched every
  hold/student pair against the successful HTTP responses. No confirmation,
  cancellation, deadline modification, or expiry occurred during this run;
  the configured hold duration was 600 seconds.

### Actual observations

The request batch ran from `2026-11-10T19:53:47.082Z` to
`2026-11-10T19:53:48.679Z` (1,597 milliseconds; November 11 in Bangladesh).

| API | Reservation requests | HTTP 201 | HTTP 409 capacity conflicts |
|---|---:|---:|---:|
| A, port 8081 | 50 | 2 | 48 |
| B, port 8082 | 50 | 8 | 42 |
| **Total** | **100** | **10** | **90** |

- Server/network errors: **0**; every reservation response was 201 or 409.
- Occupancy snapshots: **182**; maximum observed HELD + CONFIRMED: **10**.
- Both APIs reported the same final state: total **10**, HELD **10**, CONFIRMED
  **0**, available **0**.
- SQL recorded exactly **10** rows, **10** distinct hold IDs, and **10** distinct
  student IDs; all were HELD, with no inactive rows or duplicate allocations.
  All stored hold/student pairs matched the HTTP success responses.
- The earliest stored deadline was `2026-11-10T20:03:47.271770Z`, after the entire
  request batch. Occupancy was therefore monotonic in this reservation-only run:
  the final SQL total of 10 also bounds the intervals between polling snapshots.
  The capacity assertion is not based on sampling alone.
- Neither API log contained failure-level entries, unhandled exceptions, database
  errors, or deadlocks during verification.

Temporary Compose/verification scripts and raw evidence were kept under
`/private/tmp/admission-two-process-bonus-20261111/`, outside Git. The isolated
containers were stopped after verification with their volume retained. Existing
application data, services, volumes, and commits were preserved.
