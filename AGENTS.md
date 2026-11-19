# AGENTS.md

## Scope
- Repo has two major areas: `backend/` (.NET 11, minimal APIs) and `frontend/` (SolidJS + Vite + TypeScript). The root `apphost.cs` is the Aspire orchestration entry point.

### Run everything with Aspire
- `dotnet run apphost.cs` from the repo root starts the AuthTwin, API, frontend
  (`pnpm dev`), PostgreSQL 18, and RustFS together. It requires the Aspire CLI
  (`dotnet tool install -g Aspire.Cli`), a running Docker daemon, and `pnpm`.
  `aspire run` also works with Aspire CLI 17 or later.
- The AppHost is the file-based `apphost.cs`; it registers the API and AuthTwin by path with
  `AddDotnetProject`, so there is no AppHost project or `ProjectReference` to keep in sync.
- Ports are pinned to 8190 (AuthTwin), 5155 (API), and 3001 (frontend) to match
  `frontend/public/env.js`; change them in both places if you move a service.

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
- Prefer native browser elements for UI primitives where possible; use the Lucide icon set
  (`~icons/lucide/*`) instead of text glyphs as icons.
- Routes: `/` composer, `/links` owner links list (authenticated), `/s/:packageId` share gate +
  document, `*` not found. Route wiring lives in `src/shared/routes/route-tree.tsx`.
- Guests can create links, but only authenticated users can list or delete them.
- A successful open returns a short-lived, package-wide `X-Access-Session` bearer token (15 minutes)
  used for re-fetches and downloads; it is stored in `sessionStorage`, never in a URL, and is cleared on
  a download 401 or package-gone response. See `frontend/AGENTS.md`.
- Optional client-side encryption uses one passphrase per package, a salt derived from the package id,
  the `SIE3` frame container (4 MiB frames, deterministic per-item sealing) and per-item framed `meta`;
  the API only ever sees opaque bytes and the `X-Unlock-Proof` verifier. See `frontend/AGENTS.md` and
  `docs/crypto-v2.md`.

### Backend (.NET)
- NuGet versions for backend projects are centralized in `backend/Directory.Packages.props` (Central Package Management). Project files list packages without a `Version`; add or bump versions only there. Check for updates with `dotnet list kroet.ShareIt.slnx package --outdated` from `backend/`.
- The AppHost's Aspire package versions live in the `#:package` directives at the top of `apphost.cs`; keep them in lockstep and check them with `dotnet list apphost.cs package --outdated` from the repo root.
- `kroet.ShareIt.Api/Dockerfile` copies `Directory.Packages.props` before `dotnet restore`; add any new central build file to that restore layer too.
- Endpoint integration tests boot the real API with `WebApplicationFactory<Program>` (`kroet.ShareIt.Api.IntegrationTest/Endpoints/`) against the real PostgreSQL and RustFS containers. They share those containers, reset the database per test, and use a unique S3 bucket per test; authentication is provided by the `X-Test-User` header (see `Infrastructure/ShareItApiTestBase.cs`).
- The endpoint, Postgres, and S3 integration tests require a running Docker daemon (Testcontainers). The S3 tests use a RustFS container.
- Test clocks use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` (version in `Directory.Packages.props`); advance it with `SetUtcNow`/`Advance` rather than writing a custom `TimeProvider`.
- When an OTLP endpoint is configured, the API pushes OpenTelemetry metrics, traces and logs via `OTEL_EXPORTER_OTLP_ENDPOINT` (see `docs/observability.md`). It exposes no metrics endpoint. Database-derived gauges come from the SQL store only and refresh only when metrics export is enabled. Metric labels are aggregate-only: never add package, user or content identifiers as labels. Traces and logs may contain opaque package/content IDs from request URLs, but never tokens, unlock proofs, user/guest IDs, filenames or content.

## Code Style Guidelines

### General
- Follow existing patterns in each area (backend vs frontend).
- Keep changes scoped and consistent with the surrounding file.
- Prefer small, well-named helper methods over large methods when logic grows.

### Data and API boundaries
- The backend exposes REST endpoints under `/api/v1/content`.
- The frontend can generate an api client by calling `pnpm generate:open-api`

## What to do when uncertain
- Read a nearby file and follow its patterns instead of inventing new ones.
- Keep changes minimal and focused; avoid large refactors without request.
- If new tooling is needed, add it explicitly and document it here.
