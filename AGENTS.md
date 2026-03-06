# AGENTS.md

## Scope
- Repo has two major areas: `backend/` (.NET 10, minimal APIs) and `frontend/` (SolidJS + Vite + TypeScript).

### Frontend (SolidJS + Vite)
- Tailwind CSS is the primary styling system; prefer utility classes over custom CSS when possible.
- UI primitives come from `@ark-ui/solid` (see https://ark-ui.com/llms.txt).

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
