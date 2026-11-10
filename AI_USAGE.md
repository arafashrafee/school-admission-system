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
