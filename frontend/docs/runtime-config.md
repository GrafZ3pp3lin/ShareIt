# Runtime Configuration

The app now supports runtime configuration through `public/env.js`.

## How it works

- `index.html` loads `/env.js` before the app starts.
- `window.env` values are read first.
- If a runtime value is missing, the app falls back to the matching `VITE_*` variable.
- If neither is set, defaults are used.

Priority order:

1. `window.env` in `/env.js`
2. `import.meta.env.VITE_*`
3. Built-in defaults

## Supported keys (`window.env`)

- `ApiBaseUrl`
- `AuthAuthority`
- `AuthAuthorityPasswordReset`
- `AuthClientId`
- `AuthRedirectUri`
- `AuthPostLogoutRedirectUri`
- `AuthScope`

## Deployment pattern

- Build once.
- Replace `dist/env.js` per environment (dev/stage/prod).
- Keep secrets out of `env.js`; this file is public in the browser.
