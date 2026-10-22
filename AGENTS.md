# AGENTS.md

## Scope
- Repo has two major areas: `backend/` (.NET 10, minimal APIs) and `frontend/` (SolidJS + Vite + TypeScript).

### Frontend (SolidJS + Vite)
- Tailwind CSS is the primary styling system; prefer utility classes over custom CSS when possible.
- Prefer native browser elements for UI primitives where possible.

### Backend (.NET)
- Endpoint integration tests boot the real API with `WebApplicationFactory<Program>` (`kroet.ShareIt.Api.IntegrationTest/Endpoints/`). They select the in-memory storage implementations purely via configuration and authenticate via the `X-Test-User` header (see `Infrastructure/ShareItApiFactory.cs`); no S3/Postgres/identity provider is needed.
- The Postgres integration tests additionally require a running Docker daemon (Testcontainers).

## Code Style Guidelines

### General
- Follow existing patterns in each area (backend vs frontend).
- Keep changes scoped and consistent with the surrounding file.
- Prefer small, well-named helper methods over large methods when logic grows.

### Data and API boundaries
- The backend exposes REST endpoints under `/api/v1/content`.
- The frontend can generate an api client by calling `npm generate:open-api`

## What to do when uncertain
- Read a nearby file and follow its patterns instead of inventing new ones.
- Keep changes minimal and focused; avoid large refactors without request.
- If new tooling is needed, add it explicitly and document it here.
