# AGENTS.md

## Scope
- Repo has two major areas: `backend/` (.NET 10, minimal APIs) and `frontend/` (SolidJS + Vite + TypeScript).

### Auth twin
- `backend/kroet.ShareIt.AuthTwin` is a local Keycloak stand-in for development. Start it with
  `dotnet run --project backend/kroet.ShareIt.AuthTwin`; it listens on `http://localhost:8190/realms/shareit`.
- The API's `http` launch profile and `frontend/public/env.js` point at it by default. Use the API's
  `keycloak` launch profile to talk to the real Keycloak again.
- It models only the OIDC endpoints this app uses (discovery, JWKS, authorization code + PKCE + DPoP,
  token, refresh, logout) plus a loopback-only `/control` API. See its README before changing auth
  configuration.

### Frontend (SolidJS + Vite)
- Tailwind CSS is the primary styling system; prefer utility classes over custom CSS when possible.
- Prefer native browser elements for UI primitives where possible.

### Backend (.NET)
- Endpoint integration tests boot the real API with `WebApplicationFactory<Program>` (`kroet.ShareIt.Api.IntegrationTest/Endpoints/`) against the real PostgreSQL and RustFS containers. They share those containers, reset the database per test, and use a unique S3 bucket per test; authentication is provided by the `X-Test-User` header (see `Infrastructure/ShareItApiTestBase.cs`).
- The endpoint, Postgres, and S3 integration tests require a running Docker daemon (Testcontainers). The S3 tests use a RustFS container.

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
