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
