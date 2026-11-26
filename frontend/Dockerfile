# syntax=docker/dockerfile:1

# Build stage: Node 26.13.1 on Debian (glibc). Deliberately NOT Alpine: the
# @solidjs/compiler build-time dependency ships no musl binary, so an Alpine
# builder would fail to load the compiler.
# Keep the version in sync with the root .node-version (used by CI).
FROM node:26.13.1-slim AS build
WORKDIR /app

# Node 25+ no longer bundles corepack, so install pnpm directly.
# Keep in sync with the "packageManager" field in package.json.
RUN npm install --global pnpm@12.23.0

# pnpm-workspace.yaml carries install-time settings (allowBuilds,
# minimumReleaseAge, ...) that --frozen-lockfile verifies, so it must be present.
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile

COPY . .
RUN pnpm build

# Runtime: unprivileged nginx — non-root (UID 101), port 8080, minimal Alpine.
FROM nginxinc/nginx-unprivileged:alpine-slim AS final

COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
# The entrypoint only runs /docker-entrypoint.d/*.sh files with the exec bit set.
COPY --chown=101:101 docker/40-env.sh /docker-entrypoint.d/40-env.sh
RUN chmod +x /docker-entrypoint.d/40-env.sh
COPY docker/env.template.js /usr/share/nginx/html/env.template.js
# dist/env.js (copied from public/ by Vite) is overwritten at container start
# by 40-env.sh, so it must be writable by the non-root nginx user (101).
COPY --from=build --chown=101:101 /app/dist/ /usr/share/nginx/html/

EXPOSE 8080
