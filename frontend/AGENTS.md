# AGENTS.md

# Guidance for agentic coding in this repo

## Scope

- Frontend (SolidJS + Vite + TypeScript).

## Build, Lint, Test

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

Tests

- Use Vitest for frontend tests. Prefer `npm run test` if present, otherwise `npx vitest`.

Frontend packages

- Tailwind CSS is the primary styling system; prefer utility classes over custom CSS when possible.
- Icons use lucide via unplugin-icons.
- Data fetching and caching use TanStack Query.
- Routing uses TanStack Router.
- Authentication uses `oidc-client-ts` for OIDC session management.
- UI primitives come from `@ark-ui/solid` (see https://ark-ui.com/llms.txt).

## Code Style Guidelines

### General

- Follow existing patterns.
- Keep changes scoped and consistent with the surrounding file.
- Prefer small, well-named helper methods over large methods when logic grows.
- Use ASCII-only text in source files unless the file already uses Unicode.

### TypeScript / Frontend (SolidJS)

Imports and formatting

- Use ES module imports with double quotes, matching existing files.
- Keep imports ordered: external packages first, then local modules, then styles.
- Prefer oxfmt/oxlint defaults; run `npm run fmt` and `npm run lint`.

Components and naming

- Component functions are PascalCase (e.g., `function App()`).
- Local state and variables are camelCase (`const [count, setCount] = ...`).
- File names are KebabCase (e.g. `page-header.tsx`).

JSX/Solid patterns

- Use Solid primitives like `createSignal` and `render` as shown.
- Use class attributes as string literals (`class="logo"`).
- Keep JSX simple; extract components as complexity grows.

Types

- Use TypeScript for all frontend code; prefer explicit types for complex props
  and public APIs, rely on inference for local variables.

CSS

- Styles live alongside components (e.g., `app.css`, `index.css`).
- Keep selectors scoped to component usage; avoid global resets unless needed.
- If adding new styles, reuse existing class naming patterns. Prefer Tailwind utilities where practical.

## What to do when uncertain

- Read a nearby file and follow its patterns instead of inventing new ones.
- Keep changes minimal and focused; avoid large refactors without request.
- If new tooling is needed, add it explicitly and document it here.
