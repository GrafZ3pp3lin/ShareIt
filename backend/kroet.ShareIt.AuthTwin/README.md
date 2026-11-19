# ShareIt Auth Twin

A local stand-in for the Keycloak realm that the API and SPA use in development. It models only the
pieces this repository consumes:

- OIDC discovery and JWKS (`/.well-known/openid-configuration`, `/protocol/openid-connect/certs`)
- Authorization code flow with PKCE (S256) and DPoP-bound access tokens (RFC 9449)
- Token exchange and refresh
- Logout (`end_session_endpoint`)
- A small loopback-only control plane for tests and manual API calls

It does **not** model Keycloak's Admin API, userinfo, token revocation, service accounts, groups,
passwords, or mail. Tokens are signed with an RSA key generated per process, so restarting the twin
invalidates tokens and sessions from the previous run.

## Run it

```bash
dotnet run --project backend/kroet.ShareIt.AuthTwin
```

The twin listens on `http://localhost:8190` and serves the realm at
`http://localhost:8190/realms/shareit`.

The tracked development setup already points at it:

- `backend/kroet.ShareIt.Api/Properties/launchSettings.json` (`http` profile) sets
  `Auth__Authority=http://localhost:8190/realms/shareit`,
  `Auth__Audience=shareit-api-dev`, and `Auth__RequireHttpsMetadata=false`.
- `frontend/public/env.js` uses the same authority and the `shareit-spa-dev` client.

Start the twin, the API, and the SPA:

```bash
dotnet run --project backend/kroet.ShareIt.AuthTwin
dotnet run --project backend/kroet.ShareIt.Api
cd frontend && pnpm dev
```

Or start all three together from the repository root:

```bash
dotnet run apphost.cs
```

(`aspire run` also works with Aspire CLI 17 or later.)

The AppHost also starts PostgreSQL 18 (content and access-session storage) and RustFS
(S3-compatible blob storage) as Docker containers. It requires the Aspire CLI
(`dotnet tool install -g Aspire.Cli`), a running Docker daemon, and `pnpm` on `PATH`. It keeps the
ports used above (8190, 5155, 3001) so `frontend/public/env.js` needs no changes.

To point the API at the real Keycloak again, use the `keycloak` launch profile
(`dotnet run --project backend/kroet.ShareIt.Api --launch-profile keycloak`) and set
`Auth:Authority` in your local `appsettings.Development.json`.

## Sign in

Open `http://localhost:3001` and click **Sign in**. The login page lists the users from
[`realm-shareit.json`](realm-shareit.json); click one to sign in. No password is checked.

The seeded user is:

| User             | Name     |
|------------------|----------|
| `dev@shareit.local` | Dev User |

Add users or change the SPA client's redirect URIs, web origins, scopes, and audience mapper in
[`realm-shareit.json`](realm-shareit.json). The file is intentionally strict: an unknown key or a
value the twin does not model stops startup instead of being ignored silently.

## Control plane

The control plane is available only to loopback callers and lives under `/control`:

- `GET /control/users` – seeded users, including their generated `sub` (UUID)
- `GET /control/users/{id}`
- `POST /control/users/{id}/tokens` – issue tokens without a browser:
  ```json
  {
    "clientId": "shareit-spa-dev",
    "jkt": "the-DPoP-key-thumbprint",
    "scope": "openid profile",
    "issuedAt": "2026-01-01T00:00:00Z",
    "expiresIn": 60
  }
  ```
  `jkt`, `issuedAt`, and `expiresIn` are optional; `jkt` is required for the DPoP-bound SPA client.
- `POST /control/users/{id}/revoke-sessions` – end the user's SSO sessions

The issued access token carries `cnf.jkt`, so a caller still needs a matching DPoP proof when calling
the API. This is mainly useful for tests that can generate the key pair themselves.

## Tests

The twin has an end-to-end test project:

```bash
dotnet test backend/kroet.ShareIt.AuthTwin.Test
```

It covers discovery/JWKS, the login form, authorization code + PKCE + DPoP exchange, refresh, logout,
and the control plane.
