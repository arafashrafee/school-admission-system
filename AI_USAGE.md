# AI Usage & Collaboration Disclosure

## Overview
The original project disclosure credits **Antigravity** with initial architecture,
scaffolding, and infrastructure verification. **Codex** continued the existing
repository for Phase 2, its API-contract hotfix, the Phase 3 frontend, and Phase 4 QA.
The database/concurrency implementation already existed when Codex continued; this
record does not attribute unrecorded work to a human or another tool.

No other AI assistant was used during the recorded Codex work. Docker, Git, npm,
TypeScript, xUnit, Node's test runner, and Playwright/Chrome were verification tools.
Reported browser checks were agent-driven automation, not claims of unperformed
human manual testing or a separate human reviewer.

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

## Phase 3: Functional React Frontend
- Codex implemented the student identity, available classes, My applications, countdown, simulated confirmation, cancellation dialog, and public demo Admin offering workflow using the existing React/TypeScript/CSS/Lucide stack.
- Added a typed and validated assignment API client, local identity persistence, abortable polling, stale identity response protection, focused components, accessible feedback, and responsive layouts. No fake data or real payment service was added.
- Added focused tests using Node's built-in runner, TypeScript, and React server rendering. Browser testing exposed a native fetch receiver issue, which was corrected and protected by a regression test.
- Updated Vite from the scaffold's vulnerable 5.x line to compatible patched 6.4.x; retained React 18 and avoided additional application UI or test frameworks.
- Verification includes frontend tests/build, the unchanged backend regression suite, Docker Compose, and an isolated real Chrome session driven through Playwright. Browser expiry verification uses a shortened deadline only on its temporary QA hold; application timers remain server-driven.
- Final checks: 15 frontend tests passed, TypeScript and production build passed locally and inside Docker, and all 31 existing backend tests passed. The dependency audit reported zero vulnerabilities.
- Real Chrome verification covered Admin creation/duplicate errors, valid student onboarding, reserve/cancel/reapply/expire/confirm, full-class disabling, identity isolation and clearing, reload/reopened-tab persistence, countdown continuity, and offline recovery. All three screens were checked at widths 320, 390, 768, and 1440 with no horizontal overflow; desktop and mobile screenshots were visually reviewed. Temporary QA database records were removed without resetting persistent data.

## Phase 4: Final Quality Assurance
- Codex independently reran verification rather than relying on earlier phase summaries: a public clone at the expected starting commit was built with Docker, separate containers/ports/network, and a new PostgreSQL volume. Migrations and seed data initialized without a host .NET SDK or Node.js requirement.
- Reviewed the eight exact API operations, header-only identity, response DTOs/statuses, ownership/idempotency, transactional allocation, background expiry, and frontend persistence/error handling. No production business-logic defect was confirmed.
- Corrected evaluator documentation: public clone quickstart, Docker-only test commands, current frontend description, safe persistence instructions, actual class-row lock ordering, and commit-on-expiration behavior. Removed unused example environment settings.
- Added a test-only Compose profile that waits for backend health/migrations, strengthened environment/PDF/build-artifact ignores, and retained every original test assertion. Three additional cases race confirmation, cancellation, and the expiry sweep for past, near, and future deadlines.
- Verified 15 frontend tests plus TypeScript/production build, 34 backend tests on both the existing and isolated databases, and 23 real HTTP contract tests against the running API. The 100-request/10-seat cases require exactly 10 successes and 90 conflicts.
- SQL-only observation verified the isolated background worker persisted EXPIRED without follow-up API calls. The same UUID and status survived PostgreSQL and API/frontend container restarts.
- Chrome QA used uniquely named Phase 4 offerings and test identities for creation, validation, full/duplicate states, reservation, countdown, simulated confirmation, cancellation, expiry/reapplication, identity isolation, page reload/reopened tab, and offline recovery. All three screens were checked at 320/390/768/1440px; generated screenshots/logs remain ignored local artifacts.
- Repository/history scanning found no confidential PDFs, environment files, dependency/build outputs, or secret-pattern findings in reachable commits. Public local-demo database defaults remain intentionally documented; this is not a claim that static scanning can mathematically prove the absence of every possible secret.
- Full Chrome close/reopen with a dedicated persistent QA profile restored the same test identity and confirmed application. QA fixtures were removed only by their exact test class names/UUIDs and student IDs; the isolated stack was stopped with its volume retained. The main application remained running with original data preserved.
