# Architectural Assumptions & Design Decisions

This document outlines key engineering decisions and operational assumptions established during Phase 0 for the **School Admission System**.

---

## 1. Scope & Phasing
- **Phase 0 Scope:** Limited strictly to project scaffolding, containerization, environment configuration, database readiness verification, and initial front-to-back connectivity.
- **Business Logic Postponed:** Seat allocation, applicant forms, payment simulation, and admission lifecycle state machines will be designed and implemented in subsequent phases.

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
- The assignment excludes authentication. The identity header is an ownership check, not verified authentication. No Phase 3 work is included.

## 10. Phase 3 Browser Experience
- A birth registration number identifies a student with exactly 17 ASCII digits. No login/password is introduced. Local storage retains only that identifier; disabled storage permits a page-only session with a visible notice.
- The identifier is supplied exclusively through `X-Student-Id` for the required endpoints. Switching identities clears stale visible applications and aborts/ignores obsolete work. It is not secure authentication and shared devices require clearing identity.
- Admin access is deliberately open in this assignment. Class and shift are represented by one backend class name; no separate shift schema or hardcoded seat availability is introduced.
- Classes and the current student's applications are refetched every four seconds with cleanup and cancellation. Active countdowns use the backend UTC deadline and the browser clock; browser clock accuracy is assumed.
- Confirmation simulates payment only. Cancellation requires a dialog and supports HTTP 204. Confirmed admissions expose no cancellation action.
- The frontend uses React 18, TypeScript, Vite, existing CSS, and Lucide. Node's test runner and TypeScript support focused tests without an additional runtime framework.
- PostgreSQL data, assignment API contracts, and transactional concurrency logic are unchanged. Phase 3 does not include production authentication, deployment, or Phase 4 work.
