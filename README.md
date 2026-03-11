# GO.2.0 Wave 1 Setup

## Stack
- Frontend: React + TypeScript + Vite
- Backend: ASP.NET Core Web API
- DB: PostgreSQL (docker compose)

## Project Structure
- `frontend/` - UI app
- `backend/` - API + EF Core migrations
- `infra/` - local infrastructure files
- `.github/workflows/ci.yml` - CI pipeline

## Local Run
1. Start PostgreSQL:
```bash
docker compose -f infra/docker-compose.yml up -d
```
2. Apply migrations:
```bash
cd backend
dotnet ef database update --project src/GO2.Api/GO2.Api.csproj --startup-project src/GO2.Api/GO2.Api.csproj
```
3. Run backend:
```bash
cd backend/src/GO2.Api
dotnet run
```
4. Run frontend:
```bash
cd frontend
npm install
npm run dev
```

## Implemented in Wave 1
- Backend solution and API skeleton
- Health endpoint: `GET /health`
- Auth endpoints:
  - `POST /auth/register`
  - `POST /auth/login`
  - `POST /auth/refresh`
- Map endpoints:
  - `POST /maps/upload`
  - `GET /maps`
  - `GET /maps/{id}`
  - `GET /maps/{id}/versions`
- Tenant isolation by current user id
- EF Core models + initial migration
- Correlation ID middleware + ProblemDetails error format
- Frontend pages:
  - `/login`
  - `/register`
  - `/maps` (upload + list)
- CI for frontend/backend build and checks

