#!/bin/sh
set -eu

: "${API_BASE_URL:=http://localhost:8080}"
: "${AUTH_AUTHORITY:=}"
: "${AUTH_CLIENT_ID:=}"
: "${AUTH_REDIRECT_URI:=}"
: "${AUTH_POST_LOGOUT_REDIRECT_URI:=}"
: "${AUTH_SCOPE:=openid profile email roles}"
: "${AUTH_DPOP:=true}"

envsubst \
  '${API_BASE_URL} ${AUTH_AUTHORITY} ${AUTH_CLIENT_ID} ${AUTH_REDIRECT_URI} ${AUTH_POST_LOGOUT_REDIRECT_URI} ${AUTH_SCOPE} ${AUTH_DPOP}' \
  < /usr/share/nginx/html/env.template.js \
  > /usr/share/nginx/html/env.js
