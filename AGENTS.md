# AGENTS.md
# Guidance for agentic coding in this repo

## Scope
- Repo has two major areas: `backend/` (.NET 10, minimal APIs) and `frontend/` (SolidJS + Vite + TypeScript).
- No Cursor rules found in `.cursor/rules/` or `.cursorrules`.
- No Copilot rules found in `.github/copilot-instructions.md`.

## Build, Lint, Test

### Frontend (SolidJS + Vite)
- Install deps: `npm install` (or `pnpm install`, `yarn install`) in `frontend/`.
- Dev server: `npm run dev` (default http://localhost:5173).
- Build: `npm run build` (TypeScript build + Vite build).
- Preview build: `npm run preview`.
- Lint: `npm run lint` (oxlint).
- Lint with fixes: `npm run lint:fix`.
- Format: `npm run fmt` (oxfmt).
- Format check: `npm run fmt:check`.
- Dead code check: `npm run knip`.

Single-file or scoped linting (oxlint supports paths):
- Example: `npm run lint -- src/App.tsx`
- Example: `npm run lint -- src`

There are no frontend tests configured yet.

### Backend (.NET 10, minimal API)
- Build solution: `dotnet build backend/kroet.ShareIt.Api/kroet.ShareIt.Api.sln`
- Build project: `dotnet build backend/kroet.ShareIt.Api/kroet.ShareIt.Api.csproj`
- Run API: `dotnet run --project backend/kroet.ShareIt.Api/kroet.ShareIt.Api.csproj`

Tests
- No test projects found in the repo yet.
- If/when tests exist, run all tests: `dotnet test backend/kroet.ShareIt.Api/kroet.ShareIt.Api.sln`
- Run a single test by name (when tests exist):
  `dotnet test backend/kroet.ShareIt.Api/kroet.ShareIt.Api.sln --filter FullyQualifiedName~Namespace.ClassName.TestName`
- Run a single test class (when tests exist):
  `dotnet test backend/kroet.ShareIt.Api/kroet.ShareIt.Api.sln --filter FullyQualifiedName~Namespace.ClassName`

Linting/formatting
- No explicit C# lint/format tooling is configured in the repo.
- Use `dotnet format` only if the team adds it later.

## Code Style Guidelines

### General
- Follow existing patterns in each area (backend vs frontend).
- Keep changes scoped and consistent with the surrounding file.
- Prefer small, well-named helper methods over large methods when logic grows.
- Use ASCII-only text in source files unless the file already uses Unicode.

### C# / Backend

Project conventions
- Target framework is `net10.0` with nullable reference types enabled.
- Implicit usings are enabled; only add explicit usings that are actually needed.
- File-scoped namespaces are used (e.g., `namespace kroet.ShareIt.Api.Content;`).

Imports
- Group usings with `System.*` first, then external packages, then project namespaces.
- Keep usings at top of file; avoid unused usings.

Types and naming
- PascalCase for classes, records, public methods, and properties.
- Interfaces use `I` prefix (e.g., `IStorageHandler`).
- Use `record` or immutable patterns when a type is data-only.
- Use `required` and `init` for DTOs where fields must be present.
- Use `Guid.CreateVersion7` for new IDs, following current usage.

Minimal API patterns
- Endpoints are mapped via extension methods in `ContentEndpoints` and similar.
- Use `TypedResults.*` helpers with `ProblemDetails` for errors.
- Return `Results<...>` discriminated unions as shown in existing handlers.
- Use `FromRoute`, `FromBody`, `FromForm`, `FromHeader`, `FromServices` attributes consistently.
- Route groups are versioned via `ApiVersioning` and mapped under `/api/v1/...`.

Error handling
- For validation/authorization/lookup failures return `ProblemDetails` with:
  - `Title`, `Detail`, and `Status` set explicitly.
- Use 400 for bad input, 401/403 for auth/permission, 404 for missing resources,
  and 410 for expired/consumed content (as existing endpoints do).
- Use try/catch only where needed for storage or IO boundaries; let the host handle
  unexpected exceptions and log with Serilog.

Time and identity
- Use `TimeProvider` (DI) instead of `DateTime.UtcNow` directly.
- Use `TryGetUserIdentity` for guest/auth scenarios and return a 400 if missing.

Configuration
- Add options classes under `Configuration/` and bind via DI extension methods.
- Keep options names and environment variables consistent with existing settings
  (e.g., `MongoDB__ConnectionString`).

Logging
- Serilog is configured in `Program.cs`; prefer structured logging with templates
  when adding logs.

### TypeScript / Frontend (SolidJS)

Imports and formatting
- Use ES module imports with double quotes, matching existing files.
- Keep imports ordered: external packages first, then local modules, then styles.
- Prefer oxfmt/oxlint defaults; run `npm run fmt` and `npm run lint`.

Components and naming
- Component functions are PascalCase (e.g., `function App()`).
- Local state and variables are camelCase (`const [count, setCount] = ...`).
- Keep file names aligned with component names (`App.tsx`, `index.tsx`).

JSX/Solid patterns
- Use Solid primitives like `createSignal` and `render` as shown.
- Use class attributes as string literals (`class="logo"`).
- Keep JSX simple; extract components as complexity grows.

Types
- Use TypeScript for all frontend code; prefer explicit types for complex props
  and public APIs, rely on inference for local variables.

CSS
- Styles live alongside components (e.g., `App.css`, `index.css`).
- Keep selectors scoped to component usage; avoid global resets unless needed.
- If adding new styles, reuse existing class naming patterns.

### Data and API boundaries
- The backend exposes REST endpoints under `/api/v1/content`.
- When adding new endpoints, update `Content/ContentEndpoints.cs` and use
  `Produces<T>` metadata as shown for consistent API documentation.

## Repository Layout (quick reference)
- `backend/` .NET 10 minimal API and permissions libraries
  - `kroet.ShareIt.Api/` main API project
  - `kroet.Permission.AspNetCore/` permission integration
  - `kroet.Permissions/` permissions core
- `frontend/` SolidJS + Vite app

## What to do when uncertain
- Read a nearby file and follow its patterns instead of inventing new ones.
- Keep changes minimal and focused; avoid large refactors without request.
- If new tooling is needed, add it explicitly and document it here.
