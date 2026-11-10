# AI Usage & Collaboration Disclosure

## Overview
This document records how AI tooling (Antigravity Senior Software Engineer Agent) was used in the architecture, scaffolding, and verification of the **School Admission System**.

---

## Phase 0: Infrastructure & Scaffolding

### 1. Responsibilities Assigned to AI
- Environment analysis and workspace inspection.
- Project structure layout for `backend/` (.NET 8 Web API) and `frontend/` (React + Vite + TypeScript).
- Creation of multi-stage `Dockerfile` configurations ensuring zero host dependency on .NET SDK or Node.js.
- Definition of `docker-compose.yml` service orchestration with automated health dependencies between PostgreSQL, the backend API, and the React client.
- Configuration of environment variable schemes (`HOLD_DURATION_SECONDS`, `VITE_API_URL`, PostgreSQL connection strings).
- Implementation of the `GET /health` database readiness verification endpoint.
- Creation of documentation: `README.md`, `ASSUMPTIONS.md`, `.env.example`, `.gitignore`, and `AI_USAGE.md`.

### 2. Guardrails & Human Constraints Maintained
- **Phase Discipline:** Strictly scoped to Phase 0. No business logic, mock applicants, or seat allocation rules were generated.
- **Security & Privacy:** Ensured confidential assignment specifications and PDFs are ignored from version control (`*.pdf`). Protected frontend bundles from environment secret leakage.
- **Architectural Restraint:** Kept architecture clean and lean: avoided extraneous dependencies such as Redis, distributed brokers, or microservice boilerplate.
- **Verification Priority:** Validated full compilation, image building, container networking, and endpoint health through live execution tests.

## Phase 2: Admission HTTP APIs
- Codex continued the existing uncommitted repository interface changes and implemented class creation/listing plus HTTP access to the six existing admission service operations.
- Added request validation, HTTP status mapping, response DTOs, string enum serialization, and clock-effective expiration reporting.
- Added PostgreSQL-backed HTTP integration tests and a Docker test runner, retaining the existing concurrency tests and database contents.
- Updated API and test documentation. Verification outcomes are recorded in the delivery summary and commit.

## Phase 2 Hotfix: Exact Assignment Contract
- Audited endpoints, Program registration, DTOs, HTTP tests, and generated Swagger against the supplied assignment contract. The previous Phase 2 HTTP contract used incorrect prefixed routes and excess response fields.
- Corrected all eight paths/methods, moved student identity exclusively to `X-Student-Id`, introduced exact public response DTOs, and made cancellation return an empty HTTP 204 response.
- First reproduced the mismatch with failing lifecycle and Swagger regression tests. Strengthened HTTP coverage for exact JSON keys, header validation, ownership, idempotency, UTC expiration, student isolation, and 100 parallel holds for 10 seats. Preserved all existing service concurrency tests.
- Kept PostgreSQL transactional concurrency logic and persistent data intact. Verification includes the full test suite, Docker build/start, health, Swagger, and requests against all eight live endpoints.
- Scoped this change to the Phase 2 contract hotfix; no Phase 3 implementation.
- Final verification: 31/31 tests passed (8 preserved service concurrency tests and 23 HTTP contract tests); the same 23/23 HTTP tests also passed against the running Docker backend. Live Swagger matched all eight operations and exact success schemas; `/health` returned HTTP 200. The malformed-JSON regression exposed and fixed a development binding exception being converted to HTTP 500.
- Stabilized test infrastructure after a repeat run exposed aggregate PostgreSQL connection exhaustion: HTTP tests now reuse the existing service tests' pool configuration, and test classes run sequentially. Each concurrency test still sends 100 parallel requests and retains its exact capacity assertions. No production concurrency code or existing service test assertions changed.
